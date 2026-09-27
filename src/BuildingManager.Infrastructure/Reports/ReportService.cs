using BuildingManager.Core.Billing;
using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Reports;

public record AgingRow(
    int LeaseId, int ChargeId, string Property, string Unit, string Tenant, string? Phone,
    string Description, DateOnly DueDate, decimal Amount, decimal Paid, decimal Outstanding,
    int DaysOverdue, AgingBucket Bucket, string BucketLabel);

public record LedgerEntry(DateOnly Date, string Kind, int Id, string Description, decimal Charge, decimal Payment, decimal Balance, bool IsVoided);

public record Dashboard(decimal TotalOutstanding, decimal TotalOverdue, int OverdueLeases, decimal TenantCredit, int ActiveLeases, int Units);

public class ReportService(AppDbContext db)
{
    /// <summary>
    /// Every charge due by <paramref name="asOf"/> that was unpaid (or partly paid) on that date, including
    /// ones still within their grace period. Payments are replayed oldest-first using only money received
    /// by <paramref name="asOf"/>, so reports for past dates aren't affected by payments made afterwards.
    /// </summary>
    public async Task<List<AgingRow>> GetOpenChargesAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var charges = await db.Charges
            .Where(c => !c.IsVoided && c.DueDate <= asOf)
            .Select(c => new
            {
                c.LeaseId, c.Id, c.Description, c.DueDate, c.Amount,
                Property = c.Lease!.Unit!.Property!.Name,
                Unit = c.Lease.Unit.Name,
                Tenant = c.Lease.Tenant!.FullName,
                c.Lease.Tenant.Phone,
                c.Lease.GracePeriodDays,
            })
            .ToListAsync(ct);
        var paymentsByLease = (await db.Payments
                .Where(p => !p.IsVoided && p.PaymentDate <= asOf)
                .Select(p => new { p.LeaseId, p.Id, p.PaymentDate, p.Amount })
                .ToListAsync(ct))
            .ToLookup(p => p.LeaseId, p => new UnappliedPayment(p.Id, p.PaymentDate, p.Amount));

        var rows = new List<AgingRow>();
        foreach (var lease in charges.GroupBy(c => c.LeaseId))
        {
            var outstanding = PaymentAllocator.OutstandingByCharge(
                lease.Select(c => new OpenCharge(c.Id, c.DueDate, c.Amount)).ToList(), paymentsByLease[lease.Key]);

            foreach (var x in lease.Where(c => outstanding[c.Id] > 0))
            {
                var owed = outstanding[x.Id];
                var bucket = Aging.BucketFor(x.DueDate, x.GracePeriodDays, asOf);
                rows.Add(new AgingRow(x.LeaseId, x.Id, x.Property, x.Unit, x.Tenant, x.Phone, x.Description,
                    x.DueDate, x.Amount, x.Amount - owed, owed, Aging.DaysOverdue(x.DueDate, asOf), bucket, Aging.Label(bucket)));
            }
        }
        return rows.OrderBy(r => r.Property).ThenBy(r => r.Unit).ThenBy(r => r.DueDate).ToList();
    }

    public async Task<Dashboard> GetDashboardAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var open = await GetOpenChargesAsync(asOf, ct);
        var overdue = open.Where(r => r.Bucket != AgingBucket.Current).ToList();
        var credit = await db.Payments.Where(p => !p.IsVoided)
            .SumAsync(p => p.Amount - (p.Allocations.Sum(a => (decimal?)a.Amount) ?? 0), ct);
        var activeLeases = await db.Leases.CountAsync(l => l.Status == Core.Entities.LeaseStatus.Active, ct);
        var units = await db.Units.CountAsync(ct);

        return new Dashboard(open.Sum(r => r.Outstanding), overdue.Sum(r => r.Outstanding),
            overdue.Select(r => r.LeaseId).Distinct().Count(), credit, activeLeases, units);
    }

    /// <summary>Chronological statement of charges and payments with a running balance.</summary>
    public async Task<List<LedgerEntry>> GetLedgerAsync(int leaseId, CancellationToken ct = default)
    {
        var charges = await db.Charges.Where(c => c.LeaseId == leaseId)
            .Select(c => new { c.Id, Date = c.DueDate, c.Description, c.Amount, c.IsVoided, c.CreatedAt })
            .ToListAsync(ct);
        var payments = await db.Payments.Where(p => p.LeaseId == leaseId)
            .Select(p => new { p.Id, Date = p.PaymentDate, p.Method, p.Reference, p.Amount, p.IsVoided, p.CreatedAt })
            .ToListAsync(ct);

        var items = charges.Select(c => (c.Date, Order: 0, c.CreatedAt, Kind: "Charge", c.Id, c.Description, Charge: c.Amount, Payment: 0m, c.IsVoided))
            .Concat(payments.Select(p => (p.Date, Order: 1, p.CreatedAt, Kind: "Payment", p.Id,
                Description: $"Payment – {(p.Method == PaymentMethod.BankTransfer ? "Bank transfer" : p.Method)}{(string.IsNullOrWhiteSpace(p.Reference) ? "" : $" ({p.Reference})")}",
                Charge: 0m, Payment: p.Amount, p.IsVoided)))
            .OrderBy(x => x.Date).ThenBy(x => x.Order).ThenBy(x => x.CreatedAt);

        var balance = 0m;
        var ledger = new List<LedgerEntry>();
        foreach (var x in items)
        {
            if (!x.IsVoided) balance += x.Charge - x.Payment;
            ledger.Add(new LedgerEntry(x.Date, x.Kind, x.Id, x.Description, x.Charge, x.Payment, balance, x.IsVoided));
        }
        return ledger;
    }
}
