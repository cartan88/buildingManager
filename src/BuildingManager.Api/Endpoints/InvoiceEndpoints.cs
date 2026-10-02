using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Billing;
using BuildingManager.Infrastructure.Data;
using BuildingManager.Infrastructure.Invoicing;
using BuildingManager.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Api.Endpoints;

public record BusinessProfileInput(string Name, string? Address, string? Tin, string? Contact, string? PaymentInstructions,
    string DocumentTitle, string NumberPrefix, string? FooterNote, int DefaultDueDays);
public record IssueInvoiceInput(DateOnly IssueDate, DateOnly? DueDate, int[]? ChargeIds, string? Notes);
public record IssueBatchInput(DateOnly IssueDate, DateOnly? DueDate);
public record VoidInvoiceInput(string? Reason);

/// <summary>Business details and billing statements. Statements can be issued and voided, never edited.</summary>
public static class InvoiceEndpoints
{
    public static void MapInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("").AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (InvoiceValidationException e) { return Validation.Fail(e.Field, e.Message); }
        });

        g.MapGet("/settings/business", async (InvoiceService invoices) => await invoices.GetProfileAsync());

        g.MapPut("/settings/business", async (BusinessProfileInput input, InvoiceService invoices) =>
        {
            await invoices.SaveProfileAsync(new BusinessProfile
            {
                Name = input.Name, Address = input.Address, Tin = input.Tin, Contact = input.Contact,
                PaymentInstructions = input.PaymentInstructions, DocumentTitle = input.DocumentTitle,
                NumberPrefix = input.NumberPrefix, FooterNote = input.FooterNote, DefaultDueDays = input.DefaultDueDays,
            });
            return Results.NoContent();
        });

        // Name and logo are shown on the sign-in page, so these two are readable without signing in.
        g.MapGet("/settings/branding", async (InvoiceService invoices) => await invoices.GetBrandingAsync()).AllowAnonymous();

        g.MapGet("/settings/logo", async (InvoiceService invoices, HttpContext http) =>
        {
            if (await invoices.GetLogoAsync() is not { } logo) return Results.NotFound();
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(logo.Content, logo.ContentType);
        }).AllowAnonymous();

        // Cross-site uploads are already blocked by CrossSiteGuard's custom-header check.
        g.MapPost("/settings/logo", async (IFormFile file, InvoiceService invoices) =>
        {
            await using var stream = file.OpenReadStream();
            await invoices.SaveLogoAsync(file.FileName, stream, file.Length);
            return Results.Ok(await invoices.GetBrandingAsync());
        }).DisableAntiforgery();

        g.MapDelete("/settings/logo", async (InvoiceService invoices) =>
            await invoices.RemoveLogoAsync() ? Results.NoContent() : Results.NotFound());

        g.MapGet("/invoices", async (int? leaseId, InvoiceService invoices) => await invoices.ListAsync(leaseId));

        g.MapGet("/invoices.xlsx", async (InvoiceService invoices, BillingService billing) =>
        {
            var bytes = InvoiceRegisterExporter.Build(await invoices.ListAsync(), billing.Today);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Statements-{billing.Today:yyyy-MM-dd}.xlsx");
        });

        g.MapGet("/invoices/{id:int}/pdf", async (int id, InvoiceService invoices, HttpContext http) =>
        {
            if (await invoices.GetPdfAsync(id) is not { } pdf) return Results.NotFound();
            // Inline so it opens in a browser tab for printing, with a sensible name if saved.
            http.Response.Headers.ContentDisposition = $"inline; filename=\"{pdf.Number}.pdf\"";
            return Results.File(pdf.Pdf, "application/pdf");
        });

        // Unpaid charges a new statement can include.
        g.MapGet("/leases/{id:int}/open-charges", async (int id, AppDbContext db) =>
            await db.Charges
                .Where(c => c.LeaseId == id && !c.IsVoided)
                .Select(c => new { c.Id, c.Type, c.Description, c.DueDate, c.Amount, Paid = c.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 })
                .Where(c => c.Amount > c.Paid)
                .OrderBy(c => c.DueDate).ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.Type, c.Description, c.DueDate, c.Amount, c.Paid, Balance = c.Amount - c.Paid })
                .ToListAsync());

        g.MapPost("/leases/{id:int}/invoices", async (int id, IssueInvoiceInput input, InvoiceService invoices) =>
        {
            var invoice = await invoices.IssueAsync(id, new IssueInvoiceRequest(input.IssueDate, input.DueDate, input.ChargeIds, input.Notes));
            return Results.Created($"/api/invoices/{invoice.Id}", new { invoice.Id, invoice.Number });
        });

        g.MapPost("/invoices/batch", async (IssueBatchInput input, InvoiceService invoices) =>
        {
            var issued = await invoices.IssueForAllAsync(input.IssueDate, input.DueDate);
            return new { count = issued.Count, numbers = issued.Select(i => i.Number) };
        });

        g.MapPost("/invoices/{id:int}/void", async (int id, VoidInvoiceInput input, InvoiceService invoices) =>
        {
            await invoices.VoidAsync(id, input.Reason);
            return Results.NoContent();
        });
    }
}
