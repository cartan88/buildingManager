using BuildingManager.Core.Billing;
using BuildingManager.Core.Entities;

namespace BuildingManager.Tests;

public class RentScheduleTests
{
    private static Lease Lease(string start, int dueDay = 1, string? end = null) => new()
    {
        StartDate = DateOnly.Parse(start), EndDate = end is null ? null : DateOnly.Parse(end),
        DueDay = dueDay, Rent = 15000m,
    };

    [Fact]
    public void Calendar_month_lease_bills_each_month_through_as_of()
    {
        var periods = RentSchedule.PeriodsThrough(Lease("2026-01-01"), DateOnly.Parse("2026-03-15")).ToList();

        Assert.Equal(3, periods.Count);
        Assert.Equal(DateOnly.Parse("2026-03-01"), periods[2].DueDate);
        Assert.Equal("Rent – March 2026", periods[2].Description);
    }

    [Fact]
    public void Due_day_31_is_clamped_to_end_of_short_months()
    {
        var periods = RentSchedule.PeriodsThrough(Lease("2026-01-31", dueDay: 31), DateOnly.Parse("2026-04-30")).ToList();

        Assert.Equal(
            [DateOnly.Parse("2026-01-31"), DateOnly.Parse("2026-02-28"), DateOnly.Parse("2026-03-31"), DateOnly.Parse("2026-04-30")],
            periods.Select(p => p.DueDate));
        // Offsetting from the start date keeps later periods on the 31st instead of drifting to the 28th.
        Assert.Equal(DateOnly.Parse("2026-03-31"), periods[2].PeriodStart);
    }

    [Fact]
    public void Daily_lease_bills_one_charge_per_day_due_that_day()
    {
        var lease = Lease("2026-09-25");
        lease.Frequency = RentFrequency.Daily;

        var periods = RentSchedule.PeriodsThrough(lease, DateOnly.Parse("2026-09-27")).ToList();

        Assert.Equal([DateOnly.Parse("2026-09-25"), DateOnly.Parse("2026-09-26"), DateOnly.Parse("2026-09-27")], periods.Select(p => p.DueDate));
        Assert.All(periods, p => Assert.Equal(p.PeriodStart, p.PeriodEnd));
        Assert.Equal("Rent – 27 Sep 2026", periods[2].Description);
    }

    [Fact]
    public void Daily_lease_stops_at_end_date_and_ignores_due_day()
    {
        var lease = Lease("2026-09-01", dueDay: 15, end: "2026-09-10");
        lease.Frequency = RentFrequency.Daily;

        var periods = RentSchedule.PeriodsThrough(lease, DateOnly.Parse("2026-12-31")).ToList();

        Assert.Equal(10, periods.Count);
        Assert.Equal(DateOnly.Parse("2026-09-01"), periods[0].DueDate);
    }

    [Fact]
    public void Stops_at_lease_end_date()
    {
        var periods = RentSchedule.PeriodsThrough(Lease("2026-01-01", end: "2026-06-30"), DateOnly.Parse("2027-01-01")).ToList();

        Assert.Equal(6, periods.Count);
    }

    [Fact]
    public void First_charge_is_never_due_before_move_in()
    {
        var first = RentSchedule.PeriodsThrough(Lease("2026-01-15", dueDay: 1), DateOnly.Parse("2026-01-20")).Single();

        Assert.Equal(DateOnly.Parse("2026-01-15"), first.DueDate);
        Assert.Equal("Rent – 15 Jan 2026 to 14 Feb 2026", first.Description);
    }

    [Fact]
    public void Nothing_billed_before_lease_starts()
    {
        Assert.Empty(RentSchedule.PeriodsThrough(Lease("2026-05-01"), DateOnly.Parse("2026-04-30")));
    }
}

public class PaymentAllocatorTests
{
    private static readonly DateOnly D = DateOnly.Parse("2026-01-01");

    [Fact]
    public void Pays_oldest_charges_first_and_splits_partial_payments()
    {
        var charges = new[] { new OpenCharge(2, D.AddMonths(1), 10000), new OpenCharge(1, D, 10000) };
        var payments = new[] { new UnappliedPayment(1, D, 15000) };

        var result = PaymentAllocator.Allocate(charges, payments);

        Assert.Equal([new Allocation(1, 1, 10000), new Allocation(1, 2, 5000)], result);
    }

    [Fact]
    public void Several_payments_fill_one_charge()
    {
        var charges = new[] { new OpenCharge(1, D, 10000) };
        var payments = new[] { new UnappliedPayment(1, D, 4000), new UnappliedPayment(2, D.AddDays(3), 6000) };

        var result = PaymentAllocator.Allocate(charges, payments);

        Assert.Equal([new Allocation(1, 1, 4000), new Allocation(2, 1, 6000)], result);
    }

    [Fact]
    public void Overpayment_is_left_unapplied_as_credit()
    {
        var result = PaymentAllocator.Allocate([new OpenCharge(1, D, 5000)], [new UnappliedPayment(1, D, 8000)]);

        Assert.Equal(5000, result.Sum(a => a.Amount));
    }

    [Fact]
    public void Outstanding_by_charge_replays_payments_oldest_first()
    {
        var result = PaymentAllocator.OutstandingByCharge(
            [new OpenCharge(1, D, 10000), new OpenCharge(2, D.AddMonths(1), 10000), new OpenCharge(3, D.AddMonths(2), 10000)],
            [new UnappliedPayment(1, D, 14000)]);

        Assert.Equal(new Dictionary<int, decimal> { [1] = 0, [2] = 6000, [3] = 10000 }, result);
    }

    [Fact]
    public void Already_settled_charges_are_skipped()
    {
        var result = PaymentAllocator.Allocate(
            [new OpenCharge(1, D, 0), new OpenCharge(2, D.AddMonths(1), 3000)],
            [new UnappliedPayment(1, D, 3000)]);

        Assert.Equal([new Allocation(1, 2, 3000)], result);
    }
}

public class AgingTests
{
    private static readonly DateOnly Due = DateOnly.Parse("2026-03-01");

    [Theory]
    [InlineData("2026-03-01", 0, AgingBucket.Current)]
    [InlineData("2026-03-02", 0, AgingBucket.Days1To30)]
    [InlineData("2026-03-05", 5, AgingBucket.Current)]   // still inside grace period
    [InlineData("2026-03-07", 5, AgingBucket.Days1To30)]
    [InlineData("2026-04-15", 0, AgingBucket.Days31To60)]
    [InlineData("2026-05-20", 0, AgingBucket.Days61To90)]
    [InlineData("2026-06-30", 0, AgingBucket.Over90)]
    public void Buckets_by_days_since_due_date(string asOf, int grace, AgingBucket expected)
    {
        Assert.Equal(expected, Aging.BucketFor(Due, grace, DateOnly.Parse(asOf)));
    }
}
