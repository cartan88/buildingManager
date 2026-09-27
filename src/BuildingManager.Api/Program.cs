using System.Text.Json.Serialization;
using BuildingManager.Api;
using BuildingManager.Api.Endpoints;
using BuildingManager.Infrastructure.Billing;
using BuildingManager.Infrastructure.Data;
using BuildingManager.Infrastructure.Expenses;
using BuildingManager.Infrastructure.Invoicing;
using BuildingManager.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Allows installing as a Windows Service later (sc.exe create ...); no effect when run normally.
builder.Host.UseWindowsService();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("BuildingManager")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<ProfitAndLossService>();
builder.Services.AddHostedService<RentChargeWorker>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Single-PC app: keep the schema current automatically on startup.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

app.UseExceptionHandler();
app.UseCrossSiteGuard();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapSetupEndpoints();
api.MapLeaseEndpoints();
api.MapReportEndpoints();
api.MapInvoiceEndpoints();
api.MapExpenseEndpoints();

// React client-side routes
app.MapFallbackToFile("index.html");

app.Run();

// Lets integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
