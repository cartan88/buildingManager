using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Billing;
using BuildingManager.Infrastructure.Data;
using BuildingManager.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Tests;

/// <summary>
/// A throwaway SQL Server database per test, migrated from scratch and dropped afterwards.
/// Uses .\SQLEXPRESS unless BUILDINGMANAGER_TEST_SQL names another server.
/// </summary>
public abstract class DatabaseTest : IAsyncLifetime
{
    protected string ConnectionString { get; } =
        $"Server={Environment.GetEnvironmentVariable("BUILDINGMANAGER_TEST_SQL") ?? @".\SQLEXPRESS"};" +
        $"Database=BuildingManager_Test_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";

    protected AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    protected BillingService Billing(AppDbContext db) => new(db, TimeProvider.System);
    protected ReportService Reports(AppDbContext db) => new(db);

    public virtual async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public virtual async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>One property, one unit, one tenant and a lease on the 15th, like the manual test data.</summary>
    protected async Task<int> SeedLeaseAsync(string start = "2026-06-15", decimal rent = 12000, int dueDay = 15, int grace = 0)
    {
        await using var db = NewContext();
        var lease = new Lease
        {
            Unit = new Unit { Name = "Unit 1A", Property = new Property { Name = "Test Apartments" } },
            Tenant = new Tenant { FullName = "Juan Dela Cruz" },
            StartDate = DateOnly.Parse(start), Rent = rent, DueDay = dueDay, GracePeriodDays = grace,
        };
        db.Leases.Add(lease);
        await db.SaveChangesAsync();
        return lease.Id;
    }

    protected async Task PayAsync(int leaseId, string date, decimal amount)
    {
        await using var db = NewContext();
        db.Payments.Add(new Payment { LeaseId = leaseId, PaymentDate = DateOnly.Parse(date), Amount = amount, Method = PaymentMethod.Cash });
        await db.SaveChangesAsync();
        await Billing(db).AllocateAsync(leaseId);
    }

    protected static DateOnly D(string iso) => DateOnly.Parse(iso);
}
