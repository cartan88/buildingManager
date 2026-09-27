using BuildingManager.Core.Billing;
using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Billing;

public class BillingService(AppDbContext db, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>
    /// Creates any rent charges that are due but missing, then applies tenant credit to them.
    /// Safe to run any number of times, including concurrently.
    /// </summary>
    public async Task<int> GenerateRentChargesAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        var date = asOf ?? Today;
        var leaseIds = await db.Leases
            .Where(l => l.StartDate <= date && !(l.Status == LeaseStatus.Ended && l.EndDate == null))
            .Select(l => l.Id)
            .ToListAsync(ct);

        var created = 0;
        foreach (var leaseId in leaseIds)
        {
            await InLeaseLockAsync(leaseId, async () =>
            {
                // Re-read inside the lock so we see charges another run may have just committed.
                var lease = await db.Leases.AsNoTracking().FirstAsync(l => l.Id == leaseId, ct);
                var billed = (await db.Charges
                    .Where(c => c.LeaseId == leaseId && c.Type == ChargeType.Rent && c.PeriodStart != null && !c.VoidedByLeaseEnd)
                    .Select(c => c.PeriodStart!.Value)
                    .ToListAsync(ct)).ToHashSet();

                var missing = RentSchedule.PeriodsThrough(lease, date).Where(p => !billed.Contains(p.PeriodStart)).ToList();
                if (missing.Count == 0) return;

                db.Charges.AddRange(missing.Select(p => new Charge
                {
                    LeaseId = leaseId,
                    Type = ChargeType.Rent,
                    Description = p.Description,
                    PeriodStart = p.PeriodStart,
                    DueDate = p.DueDate,
                    Amount = lease.MonthlyRent,
                }));
                await db.SaveChangesAsync(ct);
                await AllocateAsync(leaseId, ct);
                created += missing.Count;
            }, ct);
        }
        return created;
    }

    /// <summary>Applies any unapplied payment money on a lease to its open charges, oldest first.</summary>
    public Task AllocateAsync(int leaseId, CancellationToken ct = default) => InLeaseLockAsync(leaseId, async () =>
    {
        var charges = await db.Charges
            .Where(c => c.LeaseId == leaseId && !c.IsVoided)
            .Select(c => new OpenCharge(c.Id, c.DueDate, c.Amount - (c.Allocations.Sum(a => (decimal?)a.Amount) ?? 0)))
            .ToListAsync(ct);
        var payments = await db.Payments
            .Where(p => p.LeaseId == leaseId && !p.IsVoided)
            .Select(p => new UnappliedPayment(p.Id, p.PaymentDate, p.Amount - (p.Allocations.Sum(a => (decimal?)a.Amount) ?? 0)))
            .ToListAsync(ct);

        var allocations = PaymentAllocator.Allocate(charges, payments);
        if (allocations.Count == 0) return;

        db.PaymentAllocations.AddRange(allocations.Select(a =>
            new PaymentAllocation { PaymentId = a.PaymentId, ChargeId = a.ChargeId, Amount = a.Amount }));
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task VoidPaymentAsync(Payment payment, CancellationToken ct = default) => InLeaseLockAsync(payment.LeaseId, async () =>
    {
        payment.IsVoided = true;
        // Allocations are derived bookkeeping; the voided payment itself stays on record.
        await db.PaymentAllocations.Where(a => a.PaymentId == payment.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await AllocateAsync(payment.LeaseId, ct);
    }, ct);

    public Task VoidChargeAsync(Charge charge, CancellationToken ct = default) => InLeaseLockAsync(charge.LeaseId, async () =>
    {
        charge.IsVoided = true;
        await db.PaymentAllocations.Where(a => a.ChargeId == charge.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await AllocateAsync(charge.LeaseId, ct); // freed-up money goes to the next open charge
    }, ct);

    /// <summary>
    /// Voids rent already billed for periods that start after the lease's end date (e.g. the move-out
    /// was recorded late) and re-applies any money paid towards them. Returns the number voided.
    /// </summary>
    public async Task<int> VoidRentAfterEndAsync(int leaseId, CancellationToken ct = default)
    {
        var voided = 0;
        await InLeaseLockAsync(leaseId, async () =>
        {
            var end = await db.Leases.Where(l => l.Id == leaseId).Select(l => l.EndDate).FirstOrDefaultAsync(ct);
            if (end is null) return;

            var ids = await db.Charges
                .Where(c => c.LeaseId == leaseId && c.Type == ChargeType.Rent && !c.IsVoided && c.PeriodStart > end)
                .Select(c => c.Id)
                .ToListAsync(ct);
            if (ids.Count == 0) return;

            await db.PaymentAllocations.Where(a => ids.Contains(a.ChargeId)).ExecuteDeleteAsync(ct);
            await db.Charges.Where(c => ids.Contains(c.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsVoided, true).SetProperty(c => c.VoidedByLeaseEnd, true), ct);
            await AllocateAsync(leaseId, ct); // money paid towards them becomes credit or pays other charges
            voided = ids.Count;
        }, ct);
        return voided;
    }

    private Task InLeaseLockAsync(int leaseId, Func<Task> action, CancellationToken ct) =>
        db.InAppLockAsync(AppLocks.Lease(leaseId), action, ct);
}
