using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Billing;
using BuildingManager.Infrastructure.Expenses;
using BuildingManager.Infrastructure.Reports;
using Microsoft.Net.Http.Headers;

namespace BuildingManager.Api.Endpoints;

public record CategoryInput(string Name, ExpenseCategoryKind Kind, bool IsArchived);

public static class ExpenseEndpoints
{
    public static void MapExpenseEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("").AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (ExpenseValidationException e) { return Validation.Fail(e.Field, e.Message); }
        });

        g.MapGet("/expenses", async (DateOnly? from, DateOnly? to, int? propertyId, bool? general, int? categoryId, bool? includeVoided,
                ExpenseService expenses) =>
            await expenses.ListAsync(new ExpenseFilter(from, to, propertyId, general == true, categoryId, includeVoided == true)));

        g.MapPost("/expenses", async (ExpenseInput input, ExpenseService expenses) =>
        {
            var id = await expenses.CreateAsync(input);
            return Results.Created($"/api/expenses/{id}", new { id });
        });

        g.MapPut("/expenses/{id:int}", async (int id, ExpenseInput input, ExpenseService expenses) =>
        {
            await expenses.UpdateAsync(id, input);
            return Results.NoContent();
        });

        g.MapPost("/expenses/{id:int}/void", async (int id, ExpenseService expenses) =>
        {
            await expenses.VoidAsync(id);
            return Results.NoContent();
        });

        // Cross-site uploads are already blocked by CrossSiteGuard's custom-header check.
        g.MapPost("/expenses/{id:int}/receipts", async (int id, IFormFile file, ExpenseService expenses) =>
        {
            await using var stream = file.OpenReadStream();
            return Results.Ok(await expenses.AddReceiptAsync(id, file.FileName, stream, file.Length));
        }).DisableAntiforgery();

        g.MapGet("/receipts/{id:int}", async (int id, ExpenseService expenses, HttpContext http) =>
        {
            if (await expenses.GetReceiptAsync(id) is not { } receipt) return Results.NotFound();
            // Uploaded names can contain anything; let the header type encode them safely.
            http.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = receipt.FileName }.ToString();
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(receipt.Content, receipt.ContentType);
        });

        g.MapDelete("/receipts/{id:int}", async (int id, ExpenseService expenses) =>
            await expenses.RemoveReceiptAsync(id) ? Results.NoContent() : Results.NotFound());

        g.MapGet("/expense-categories", async (ExpenseService expenses) => await expenses.CategoriesAsync());

        g.MapPost("/expense-categories", async (CategoryInput input, ExpenseService expenses) =>
        {
            var id = await expenses.SaveCategoryAsync(null, input.Name, input.Kind, input.IsArchived);
            return Results.Created($"/api/expense-categories/{id}", new { id });
        });

        g.MapPut("/expense-categories/{id:int}", async (int id, CategoryInput input, ExpenseService expenses) =>
        {
            await expenses.SaveCategoryAsync(id, input.Name, input.Kind, input.IsArchived);
            return Results.NoContent();
        });

        g.MapGet("/vendors", async (ExpenseService expenses) => await expenses.VendorNamesAsync());

        g.MapGet("/reports/pnl", async (DateOnly from, DateOnly to, PnlBasis? basis, PnlGrouping? by, int? propertyId,
            ProfitAndLossService pnl) =>
        {
            if (to < from) return Validation.Fail("to", "End date is before the start date.");
            return Results.Ok(await pnl.BuildAsync(from, to, basis ?? PnlBasis.Cash, by ?? PnlGrouping.Property, propertyId));
        });

        g.MapGet("/reports/pnl.xlsx", async (DateOnly from, DateOnly to, PnlBasis? basis, PnlGrouping? by, int? propertyId,
            ProfitAndLossService pnl, ExpenseService expenses) =>
        {
            if (to < from) return Validation.Fail("to", "End date is before the start date.");
            var b = basis ?? PnlBasis.Cash;
            var report = await pnl.BuildAsync(from, to, b, by ?? PnlGrouping.Property, propertyId);
            var rows = await expenses.ListAsync(new ExpenseFilter(from, to, propertyId));
            var bytes = ProfitAndLossExcelExporter.Build(report, rows, from, to, b);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PnL-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.xlsx");
        });
    }
}
