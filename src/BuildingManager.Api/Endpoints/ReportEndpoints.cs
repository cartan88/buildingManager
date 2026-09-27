using BuildingManager.Infrastructure.Billing;
using BuildingManager.Infrastructure.Reports;

namespace BuildingManager.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (ReportService reports, BillingService billing) =>
            await reports.GetDashboardAsync(billing.Today));

        api.MapGet("/reports/aging", async (DateOnly? asOf, ReportService reports, BillingService billing) =>
            await reports.GetOpenChargesAsync(asOf ?? billing.Today));

        api.MapGet("/reports/aging.xlsx", async (DateOnly? asOf, ReportService reports, BillingService billing) =>
        {
            var date = asOf ?? billing.Today;
            var bytes = AgingExcelExporter.Build(await reports.GetOpenChargesAsync(date), date);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Aging-{date:yyyy-MM-dd}.xlsx");
        });

        api.MapPost("/billing/run", async (BillingService billing) =>
            new { created = await billing.GenerateRentChargesAsync() });
    }
}
