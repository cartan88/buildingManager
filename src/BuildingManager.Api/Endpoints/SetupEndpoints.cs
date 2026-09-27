using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Api.Endpoints;

public record PropertyInput(string Name, string? Address, string? Notes);
public record UnitInput(string Name, decimal DefaultMonthlyRent, string? Notes);
public record TenantInput(string FullName, string? Email, string? Phone, string? Tin, string? Notes);

/// <summary>Properties, units and tenants: the reference data everything else hangs off.</summary>
public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/properties", async (AppDbContext db) =>
            await db.Properties.OrderBy(p => p.Name).Select(p => new
            {
                p.Id, p.Name, p.Address, p.Notes,
                Units = p.Units.OrderBy(u => u.Name).Select(u => new
                {
                    u.Id, u.Name, u.DefaultMonthlyRent, u.Notes,
                    HasLeases = u.Leases.Any(),
                    CurrentTenant = u.Leases.Where(l => l.Status == LeaseStatus.Active)
                        .Select(l => l.Tenant!.FullName).FirstOrDefault(),
                }),
            }).ToListAsync());

        api.MapPost("/properties", async (PropertyInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Validation.Fail("name", "Name is required.");
            var p = new Property { Name = input.Name.Trim(), Address = input.Address, Notes = input.Notes };
            db.Properties.Add(p);
            await db.SaveChangesAsync();
            return Results.Created($"/api/properties/{p.Id}", new { p.Id });
        });

        api.MapPut("/properties/{id:int}", async (int id, PropertyInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Validation.Fail("name", "Name is required.");
            var p = await db.Properties.FindAsync(id);
            if (p is null) return Results.NotFound();
            (p.Name, p.Address, p.Notes) = (input.Name.Trim(), input.Address, input.Notes);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/properties/{id:int}", async (int id, AppDbContext db) =>
        {
            if (await db.Units.AnyAsync(u => u.PropertyId == id))
                return Validation.Fail("property", "Remove its units first.");
            return await db.Properties.Where(p => p.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();
        });

        api.MapPost("/properties/{propertyId:int}/units", async (int propertyId, UnitInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Validation.Fail("name", "Unit name is required.");
            if (input.DefaultMonthlyRent < 0) return Validation.Fail("defaultMonthlyRent", "Rent can't be negative.");
            if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();
            if (await db.Units.AnyAsync(u => u.PropertyId == propertyId && u.Name == input.Name.Trim()))
                return Validation.Fail("name", "This property already has a unit with that name.");

            var u = new Unit { PropertyId = propertyId, Name = input.Name.Trim(), DefaultMonthlyRent = input.DefaultMonthlyRent, Notes = input.Notes };
            db.Units.Add(u);
            await db.SaveChangesAsync();
            return Results.Created($"/api/units/{u.Id}", new { u.Id });
        });

        api.MapPut("/units/{id:int}", async (int id, UnitInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Validation.Fail("name", "Unit name is required.");
            if (input.DefaultMonthlyRent < 0) return Validation.Fail("defaultMonthlyRent", "Rent can't be negative.");
            var u = await db.Units.FindAsync(id);
            if (u is null) return Results.NotFound();
            if (await db.Units.AnyAsync(x => x.PropertyId == u.PropertyId && x.Id != id && x.Name == input.Name.Trim()))
                return Validation.Fail("name", "This property already has a unit with that name.");
            (u.Name, u.DefaultMonthlyRent, u.Notes) = (input.Name.Trim(), input.DefaultMonthlyRent, input.Notes);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/units/{id:int}", async (int id, AppDbContext db) =>
        {
            if (await db.Leases.AnyAsync(l => l.UnitId == id))
                return Validation.Fail("unit", "This unit has lease history and can't be deleted.");
            return await db.Units.Where(u => u.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();
        });

        api.MapGet("/tenants", async (AppDbContext db) =>
            await db.Tenants.OrderBy(t => t.FullName).Select(t => new
            {
                t.Id, t.FullName, t.Email, t.Phone, t.Tin, t.Notes, t.IsActive,
                ActiveLeases = t.Leases.Count(l => l.Status == LeaseStatus.Active),
                TotalLeases = t.Leases.Count,
            }).ToListAsync());

        api.MapPost("/tenants", async (TenantInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.FullName)) return Validation.Fail("fullName", "Name is required.");
            var t = new Tenant { FullName = input.FullName.Trim(), Email = input.Email, Phone = input.Phone, Tin = input.Tin, Notes = input.Notes };
            db.Tenants.Add(t);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tenants/{t.Id}", new { t.Id });
        });

        api.MapPut("/tenants/{id:int}", async (int id, TenantInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.FullName)) return Validation.Fail("fullName", "Name is required.");
            var t = await db.Tenants.FindAsync(id);
            if (t is null) return Results.NotFound();
            (t.FullName, t.Email, t.Phone, t.Tin, t.Notes) = (input.FullName.Trim(), input.Email, input.Phone, input.Tin, input.Notes);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/tenants/{id:int}/deactivate", async (int id, AppDbContext db) =>
        {
            var t = await db.Tenants.FindAsync(id);
            if (t is null) return Results.NotFound();
            if (await db.Leases.AnyAsync(l => l.TenantId == id && l.Status == LeaseStatus.Active))
                return Validation.Fail("tenant", "This tenant has an active lease. End it first.");
            t.IsActive = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/tenants/{id:int}/activate", async (int id, AppDbContext db) =>
        {
            var t = await db.Tenants.FindAsync(id);
            if (t is null) return Results.NotFound();
            t.IsActive = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/tenants/{id:int}", async (int id, AppDbContext db) =>
        {
            if (await db.Leases.AnyAsync(l => l.TenantId == id))
                return Validation.Fail("tenant", "This tenant has lease history and can't be deleted. Mark them inactive instead.");
            return await db.Tenants.Where(t => t.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}

internal static class Validation
{
    public static IResult Fail(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
