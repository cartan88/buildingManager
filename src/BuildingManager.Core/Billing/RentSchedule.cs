using System.Globalization;
using BuildingManager.Core.Entities;

namespace BuildingManager.Core.Billing;

public record RentPeriod(DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate, string Description);

/// <summary>
/// Works out which rent charges a lease should have: one per month, or one per day for a daily lease. Pure logic: no database access,
/// so the generator can compare this against existing charges and only add what's missing.
/// </summary>
public static class RentSchedule
{
    private static readonly CultureInfo PhCulture = CultureInfo.GetCultureInfo("en-PH");

    /// <summary>All rent periods that have started or fallen due on or before <paramref name="asOf"/>.</summary>
    public static IEnumerable<RentPeriod> PeriodsThrough(Lease lease, DateOnly asOf) =>
        lease.Frequency == RentFrequency.Daily ? DailyPeriodsThrough(lease, asOf) : MonthlyPeriodsThrough(lease, asOf);

    /// <summary>Each day is its own period, due that same day.</summary>
    private static IEnumerable<RentPeriod> DailyPeriodsThrough(Lease lease, DateOnly asOf)
    {
        var last = lease.EndDate is { } end && end < asOf ? end : asOf;
        for (var day = lease.StartDate; day <= last; day = day.AddDays(1))
            yield return new RentPeriod(day, day, day, $"Rent – {day.ToString("d MMM yyyy", PhCulture)}");
    }

    private static IEnumerable<RentPeriod> MonthlyPeriodsThrough(Lease lease, DateOnly asOf)
    {
        for (var i = 0; ; i++)
        {
            // Always offset from StartDate (not the previous period) so a 31st start doesn't drift to the 28th.
            var periodStart = lease.StartDate.AddMonths(i);
            if (lease.EndDate is { } end && periodStart > end) yield break;

            var dueDate = DueDateInMonth(periodStart.Year, periodStart.Month, lease.DueDay);
            if (dueDate < lease.StartDate) dueDate = lease.StartDate; // first charge can't be due before move-in

            if (periodStart > asOf && dueDate > asOf) yield break;

            var periodEnd = lease.StartDate.AddMonths(i + 1).AddDays(-1);
            if (lease.EndDate is { } e && periodEnd > e) periodEnd = e;

            yield return new RentPeriod(periodStart, periodEnd, dueDate, Describe(periodStart, periodEnd));
        }
    }

    public static DateOnly DueDateInMonth(int year, int month, int dueDay)
    {
        var day = Math.Clamp(dueDay, 1, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }

    private static string Describe(DateOnly start, DateOnly end)
    {
        var isCalendarMonth = start.Day == 1 && end == start.AddMonths(1).AddDays(-1);
        return isCalendarMonth
            ? $"Rent – {start.ToString("MMMM yyyy", PhCulture)}"
            : $"Rent – {start.ToString("d MMM yyyy", PhCulture)} to {end.ToString("d MMM yyyy", PhCulture)}";
    }
}
