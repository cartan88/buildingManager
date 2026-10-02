using System.Text.Json.Serialization;
using BuildingManager.Api;
using BuildingManager.Api.Auth;
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
builder.Services.AddAppAuthentication();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Single-PC app: keep the schema current automatically on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Forgot the password: `dotnet run --project src/BuildingManager.Api -- --reset-login`
    if (args.Contains("--reset-login"))
    {
        var removed = await AuthEndpoints.ResetLoginAsync(db);
        Console.WriteLine(removed > 0
            ? "Login removed. Start the app normally and open it to set a new username and password. No other data was changed."
            : "There was no login to remove.");
        return;
    }
}

app.UseExceptionHandler();
app.UseCrossSiteGuard();
app.UseDefaultFiles();
app.UseStaticFiles(); // the React app itself has no data in it; it shows the sign-in page until signed in
app.UseAuthentication();
app.UseAuthorization();

// Everything under /api needs a signed-in user unless an endpoint says otherwise (AllowAnonymous).
var api = app.MapGroup("/api").RequireAuthorization();
api.MapAuthEndpoints();
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
