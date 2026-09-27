using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Data;

public static class AppLocks
{
    public static string Lease(int leaseId) => $"BuildingManager.Lease.{leaseId}";
    public const string InvoiceNumbers = "BuildingManager.InvoiceNumbers";

    /// <summary>
    /// Runs <paramref name="action"/> in a transaction holding an exclusive SQL Server app lock on
    /// <paramref name="resource"/>, so concurrent requests and the background worker can't interleave
    /// read-then-write work on the same data. Re-entrant: nested calls join the open transaction.
    /// </summary>
    public static async Task InAppLockAsync(this AppDbContext db, string resource, Func<Task> action, CancellationToken ct = default)
    {
        var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
                IF @result < 0 THROW 50000, 'Timed out waiting for another operation to finish. Please try again.', 1;
                """, ct);
            await action();
            if (tx is not null) await tx.CommitAsync(ct);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }
}
