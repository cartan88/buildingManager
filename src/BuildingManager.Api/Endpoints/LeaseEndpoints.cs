using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Billing;
using BuildingManager.Infrastructure.Data;
using BuildingManager.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Api.Endpoints;

public record LeaseInput(int UnitId, int TenantId, DateOnly StartDate, DateOnly? EndDate, decimal MonthlyRent,
    int DueDay, int GracePeriodDays, decimal SecurityDeposit, string? Notes);
public record LeaseUpdate(DateOnly? EndDate, decimal MonthlyRent, int DueDay, int GracePeriodDays, decimal SecurityDeposit, string? Notes);
public record EndLeaseInput(DateOnly EndDate);
public record ChargeInput(ChargeType Type, string Description, DateOnly DueDate, decimal Amount);
public record PaymentInput(DateOnly PaymentDate, decimal Amount, PaymentMethod Method, string? Reference, string? Notes);

public static class LeaseEndpoints
{
    public static void MapLeaseEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/leases", async (AppDbContext db) =>
            await db.Leases
                .OrderBy(l => l.Status).ThenBy(l => l.Unit!.Property!.Name).ThenBy(l => l.Unit!.Name)
                .Select(l => new
                {
                    l.Id, l.Status, l.StartDate, l.EndDate, l.MonthlyRent, l.DueDay,
                    Property = l.Unit!.Property!.Name, Unit = l.Unit.Name, Tenant = l.Tenant!.FullName,
                    Balance = (l.Charges.Where(c => !c.IsVoided).Sum(c => (decimal?)c.Amount) ?? 0)
                              - (l.Payments.Where(p => !p.IsVoided).Sum(p => (decimal?)p.Amount) ?? 0),
                }).ToListAsync());

        api.MapGet("/leases/{id:int}", async (int id, AppDbContext db, ReportService reports) =>
        {
            var lease = await db.Leases
                .Where(l => l.Id == id)
                .Select(l => new
                {
                    l.Id, l.UnitId, l.TenantId, l.Status, l.StartDate, l.EndDate, l.MonthlyRent, l.DueDay,
                    l.GracePeriodDays, l.SecurityDeposit, l.Notes,
                    Property = l.Unit!.Property!.Name, Unit = l.Unit.Name,
                    Tenant = l.Tenant!.FullName, l.Tenant.Phone, l.Tenant.Email,
                }).FirstOrDefaultAsync();
            if (lease is null) return Results.NotFound();

            var ledger = await reports.GetLedgerAsync(id);
            return Results.Ok(new { lease, ledger, balance = ledger.LastOrDefault()?.Balance ?? 0 });
        });

        api.MapPost("/leases", async (LeaseInput input, AppDbContext db, BillingService billing) =>
        {
            if (Validate(input.StartDate, input.EndDate, input.MonthlyRent, input.DueDay, input.GracePeriodDays, input.SecurityDeposit) is { } error)
                return error;
            if (!await db.Units.AnyAsync(u => u.Id == input.UnitId)) return Validation.Fail("unitId", "Unit not found.");
            if (!await db.Tenants.AnyAsync(t => t.Id == input.TenantId)) return Validation.Fail("tenantId", "Tenant not found.");
            if (await db.Leases.AnyAsync(l => l.UnitId == input.UnitId && l.Status == LeaseStatus.Active))
                return Validation.Fail("unitId", "This unit already has an active lease. End it first.");

            var lease = new Lease
            {
                UnitId = input.UnitId, TenantId = input.TenantId, StartDate = input.StartDate, EndDate = input.EndDate,
                MonthlyRent = input.MonthlyRent, DueDay = input.DueDay, GracePeriodDays = input.GracePeriodDays,
                SecurityDeposit = input.SecurityDeposit, Notes = input.Notes,
            };
            db.Leases.Add(lease);
            await db.SaveChangesAsync();
            await billing.GenerateRentChargesAsync(); // bill any periods already due (e.g. a back-dated lease)
            return Results.Created($"/api/leases/{lease.Id}", new { lease.Id });
        });

        // Term changes only affect rent charges generated from now on, except that shortening the term
        // voids rent already billed for periods after the new end date.
        api.MapPut("/leases/{id:int}", async (int id, LeaseUpdate input, AppDbContext db, BillingService billing) =>
        {
            var lease = await db.Leases.FindAsync(id);
            if (lease is null) return Results.NotFound();
            if (Validate(lease.StartDate, input.EndDate, input.MonthlyRent, input.DueDay, input.GracePeriodDays, input.SecurityDeposit) is { } error)
                return error;

            (lease.EndDate, lease.MonthlyRent, lease.DueDay, lease.GracePeriodDays, lease.SecurityDeposit, lease.Notes) =
                (input.EndDate, input.MonthlyRent, input.DueDay, input.GracePeriodDays, input.SecurityDeposit, input.Notes);
            await db.SaveChangesAsync();
            await billing.VoidRentAfterEndAsync(id);
            await billing.GenerateRentChargesAsync(); // re-bills periods if the term was extended
            return Results.NoContent();
        });

        api.MapPost("/leases/{id:int}/end", async (int id, EndLeaseInput input, AppDbContext db, BillingService billing) =>
        {
            var lease = await db.Leases.FindAsync(id);
            if (lease is null) return Results.NotFound();
            if (input.EndDate < lease.StartDate) return Validation.Fail("endDate", "End date is before the lease started.");

            lease.EndDate = input.EndDate;
            lease.Status = LeaseStatus.Ended;
            await db.SaveChangesAsync();
            await billing.VoidRentAfterEndAsync(id); // move-out recorded late: drop rent billed after it
            await billing.GenerateRentChargesAsync(); // catch up any periods before the end date
            return Results.NoContent();
        });

        api.MapPost("/leases/{id:int}/charges", async (int id, ChargeInput input, AppDbContext db, BillingService billing) =>
        {
            if (!await db.Leases.AnyAsync(l => l.Id == id)) return Results.NotFound();
            if (input.Amount <= 0) return Validation.Fail("amount", "Amount must be more than zero.");
            if (string.IsNullOrWhiteSpace(input.Description)) return Validation.Fail("description", "Description is required.");

            // Manual charges have no PeriodStart, so they never collide with auto-generated rent.
            var charge = new Charge { LeaseId = id, Type = input.Type, Description = input.Description.Trim(), DueDate = input.DueDate, Amount = input.Amount };
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            await billing.AllocateAsync(id); // use up any tenant credit
            return Results.Created($"/api/charges/{charge.Id}", new { charge.Id });
        });

        api.MapPost("/charges/{id:int}/void", async (int id, AppDbContext db, BillingService billing) =>
        {
            var charge = await db.Charges.FindAsync(id);
            if (charge is null) return Results.NotFound();
            if (charge.IsVoided) return Results.NoContent();
            await billing.VoidChargeAsync(charge);
            return Results.NoContent();
        });

        api.MapPost("/leases/{id:int}/payments", async (int id, PaymentInput input, AppDbContext db, BillingService billing) =>
        {
            if (!await db.Leases.AnyAsync(l => l.Id == id)) return Results.NotFound();
            if (input.Amount <= 0) return Validation.Fail("amount", "Amount must be more than zero.");

            var payment = new Payment
            {
                LeaseId = id, PaymentDate = input.PaymentDate, Amount = input.Amount, Method = input.Method,
                Reference = input.Reference, Notes = input.Notes,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            await billing.AllocateAsync(id);
            return Results.Created($"/api/payments/{payment.Id}", new { payment.Id });
        });

        api.MapPost("/payments/{id:int}/void", async (int id, AppDbContext db, BillingService billing) =>
        {
            var payment = await db.Payments.FindAsync(id);
            if (payment is null) return Results.NotFound();
            if (payment.IsVoided) return Results.NoContent();
            await billing.VoidPaymentAsync(payment);
            return Results.NoContent();
        });
    }

    private static IResult? Validate(DateOnly start, DateOnly? end, decimal rent, int dueDay, int grace, decimal deposit)
    {
        if (end is { } e && e < start) return Validation.Fail("endDate", "End date is before the start date.");
        if (rent <= 0) return Validation.Fail("monthlyRent", "Monthly rent must be more than zero.");
        if (dueDay is < 1 or > 31) return Validation.Fail("dueDay", "Due day must be between 1 and 31.");
        if (grace is < 0 or > 60) return Validation.Fail("gracePeriodDays", "Grace period must be 0–60 days.");
        if (deposit < 0) return Validation.Fail("securityDeposit", "Deposit can't be negative.");
        return null;
    }
}
