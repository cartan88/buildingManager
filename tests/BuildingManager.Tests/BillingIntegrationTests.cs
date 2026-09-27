using BuildingManager.Core.Billing;
using BuildingManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Tests;

public class LeaseEndTests : DatabaseTest
{
    private async Task EndLeaseAsync(int leaseId, string endDate)
    {
        await using var db = NewContext();
        var lease = await db.Leases.SingleAsync(l => l.Id == leaseId);
        lease.EndDate = D(endDate);
        lease.Status = LeaseStatus.Ended;
        await db.SaveChangesAsync();
        await Billing(db).VoidRentAfterEndAsync(leaseId);
    }

    [Fact]
    public async Task Late_recorded_move_out_voids_rent_billed_after_it_and_frees_the_money()
    {
        var leaseId = await SeedLeaseAsync();
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-27")); // Jun..Sep
        await PayAsync(leaseId, "2026-06-15", 48000); // tenant paid all four months

        await EndLeaseAsync(leaseId, "2026-08-20"); // moved out during the Aug 15 - Sep 14 period

        await using var check = NewContext();
        var sep = await check.Charges.SingleAsync(c => c.PeriodStart == D("2026-09-15"));
        Assert.True(sep.IsVoided);
        Assert.True(sep.VoidedByLeaseEnd);
        Assert.Equal(3, await check.Charges.CountAsync(c => !c.IsVoided));
        Assert.False(await check.PaymentAllocations.AnyAsync(a => a.ChargeId == sep.Id));
        // The September money is now credit to refund or apply, not stuck on a voided charge.
        var applied = await check.PaymentAllocations.SumAsync(a => a.Amount);
        Assert.Equal(36000, applied);
    }

    [Fact]
    public async Task Extending_the_lease_again_rebills_the_period_but_a_manual_void_stays_void()
    {
        var leaseId = await SeedLeaseAsync();
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));

        // July rent waived by hand.
        await using (var db = NewContext())
            await Billing(db).VoidChargeAsync(await db.Charges.SingleAsync(c => c.PeriodStart == D("2026-07-15")));

        await EndLeaseAsync(leaseId, "2026-08-20"); // ended by mistake...
        await using (var db = NewContext())
        {
            var lease = await db.Leases.SingleAsync(l => l.Id == leaseId); // ...then corrected
            lease.EndDate = D("2027-06-14");
            lease.Status = LeaseStatus.Active;
            await db.SaveChangesAsync();
            await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));
        }

        await using var check = NewContext();
        var active = await check.Charges.Where(c => !c.IsVoided).Select(c => c.PeriodStart).ToListAsync();
        Assert.Equal([D("2026-06-15"), D("2026-08-15"), D("2026-09-15")], active.Order().Select(d => d!.Value));
    }

    [Fact]
    public async Task Periods_up_to_the_end_date_are_kept()
    {
        var leaseId = await SeedLeaseAsync();
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));

        await EndLeaseAsync(leaseId, "2026-09-15"); // period starting on the end date still counts

        await using var check = NewContext();
        Assert.Equal(4, await check.Charges.CountAsync(c => !c.IsVoided));
    }
}

public class AgingAsOfTests : DatabaseTest
{
    [Fact]
    public async Task Payments_made_after_the_report_date_are_not_counted()
    {
        var leaseId = await SeedLeaseAsync(start: "2026-08-15");
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-08-31"));
        await PayAsync(leaseId, "2026-09-20", 12000);

        await using var check = NewContext();
        var asOfAugust = await Reports(check).GetOpenChargesAsync(D("2026-08-31"));
        var row = Assert.Single(asOfAugust);
        Assert.Equal(12000, row.Outstanding);
        Assert.Equal(AgingBucket.Days1To30, row.Bucket);

        Assert.Empty(await Reports(check).GetOpenChargesAsync(D("2026-09-20")));
    }

    [Fact]
    public async Task Back_dated_payment_counts_against_the_oldest_charge_on_that_date()
    {
        var leaseId = await SeedLeaseAsync(start: "2026-07-15");
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-27")); // Jul, Aug, Sep
        await PayAsync(leaseId, "2026-09-20", 12000); // recorded first, applied to July
        await PayAsync(leaseId, "2026-08-10", 12000); // entered later but received in August, applied to August

        await using var check = NewContext();
        // On 31 Aug only the 10 Aug payment existed, and it paid July: August is the one outstanding.
        var row = Assert.Single(await Reports(check).GetOpenChargesAsync(D("2026-08-31")));
        Assert.Equal(D("2026-08-15"), row.DueDate);
        Assert.Equal(12000, row.Outstanding);
    }

    [Fact]
    public async Task Partial_payments_show_as_paid_amount_on_the_row()
    {
        var leaseId = await SeedLeaseAsync(start: "2026-09-01", dueDay: 1);
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));
        await PayAsync(leaseId, "2026-09-05", 5000);

        await using var check = NewContext();
        var row = Assert.Single(await Reports(check).GetOpenChargesAsync(D("2026-09-27")));
        Assert.Equal(5000, row.Paid);
        Assert.Equal(7000, row.Outstanding);
    }
}

public class ConcurrencyTests : DatabaseTest
{
    [Fact]
    public async Task Parallel_rent_generation_never_double_bills_or_fails()
    {
        await SeedLeaseAsync();

        var runs = Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = NewContext();
            return await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));
        });
        var created = await Task.WhenAll(runs);

        Assert.Equal(4, created.Sum());
        await using var check = NewContext();
        Assert.Equal(4, await check.Charges.CountAsync());
    }

    [Fact]
    public async Task Parallel_allocation_applies_a_payment_only_once()
    {
        var leaseId = await SeedLeaseAsync();
        await using (var db = NewContext())
        {
            await Billing(db).GenerateRentChargesAsync(D("2026-09-27")); // 48,000 billed
            db.Payments.Add(new Payment { LeaseId = leaseId, PaymentDate = D("2026-09-27"), Amount = 30000, Method = PaymentMethod.Cash });
            await db.SaveChangesAsync(); // saved but not yet allocated
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = NewContext();
            await Billing(db).AllocateAsync(leaseId);
        }));

        await using var check = NewContext();
        Assert.Equal(30000, await check.PaymentAllocations.SumAsync(a => a.Amount));
    }
}
