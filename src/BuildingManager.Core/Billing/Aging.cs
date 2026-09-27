namespace BuildingManager.Core.Billing;

public enum AgingBucket { Current, Days1To30, Days31To60, Days61To90, Over90 }

public static class Aging
{
    /// <summary>
    /// A charge is overdue once today is past its due date plus the lease's grace period.
    /// Days overdue are counted from the due date itself, as is usual for aging reports.
    /// </summary>
    public static bool IsOverdue(DateOnly dueDate, int gracePeriodDays, DateOnly asOf) =>
        asOf > dueDate.AddDays(gracePeriodDays);

    public static int DaysOverdue(DateOnly dueDate, DateOnly asOf) =>
        Math.Max(0, asOf.DayNumber - dueDate.DayNumber);

    public static AgingBucket BucketFor(DateOnly dueDate, int gracePeriodDays, DateOnly asOf)
    {
        if (!IsOverdue(dueDate, gracePeriodDays, asOf)) return AgingBucket.Current;
        return DaysOverdue(dueDate, asOf) switch
        {
            <= 30 => AgingBucket.Days1To30,
            <= 60 => AgingBucket.Days31To60,
            <= 90 => AgingBucket.Days61To90,
            _ => AgingBucket.Over90,
        };
    }

    public static string Label(AgingBucket bucket) => bucket switch
    {
        AgingBucket.Current => "Not yet overdue",
        AgingBucket.Days1To30 => "1–30 days",
        AgingBucket.Days31To60 => "31–60 days",
        AgingBucket.Days61To90 => "61–90 days",
        _ => "Over 90 days",
    };
}
