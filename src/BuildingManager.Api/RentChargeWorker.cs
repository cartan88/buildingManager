using BuildingManager.Infrastructure.Billing;

namespace BuildingManager.Api;

/// <summary>Creates due rent charges at startup and then hourly. Generation is idempotent, so reruns are harmless.</summary>
public class RentChargeWorker(IServiceScopeFactory scopes, ILogger<RentChargeWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var created = await scope.ServiceProvider.GetRequiredService<BillingService>().GenerateRentChargesAsync(ct: ct);
                if (created > 0) log.LogInformation("Created {Count} rent charge(s)", created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Rent charge generation failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
