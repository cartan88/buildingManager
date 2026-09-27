using System.Net;
using System.Net.Http.Json;
using BuildingManager.Api;
using BuildingManager.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingManager.Tests;

public class ApiTests : DatabaseTest
{
    private WebApplicationFactory<Program> _factory = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            // Later registrations win: point the app at this test's database, and keep the
            // background worker from billing in the middle of a test.
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConnectionString));
            var worker = services.Single(s => s.ImplementationType == typeof(RentChargeWorker));
            services.Remove(worker);
        }));
    }

    public override async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await base.DisposeAsync();
    }

    private HttpClient TrustedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CrossSiteGuard.HeaderName, CrossSiteGuard.HeaderValue);
        return client;
    }

    [Theory]
    [InlineData("/api/payments/1/void")]
    [InlineData("/api/charges/1/void")]
    [InlineData("/api/billing/run")]
    public async Task Body_less_posts_without_the_header_are_rejected(string url)
    {
        // What a cross-site <form> or no-cors fetch from another website looks like.
        var res = await _factory.CreateClient().PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Requests_from_the_app_itself_get_through()
    {
        var res = await TrustedClient().PostAsync("/api/payments/999/void", content: null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode); // reached the endpoint
    }

    [Fact]
    public async Task Reads_do_not_need_the_header()
    {
        var res = await _factory.CreateClient().GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Renaming_a_unit_to_an_existing_name_is_a_validation_error()
    {
        var client = TrustedClient();
        var property = await (await client.PostAsJsonAsync("/api/properties", new { name = "Test Apartments" })).Content.ReadFromJsonAsync<IdResponse>();
        await client.PostAsJsonAsync($"/api/properties/{property!.Id}/units", new { name = "Unit 1A", defaultMonthlyRent = 12000 });
        var b = await (await client.PostAsJsonAsync($"/api/properties/{property.Id}/units", new { name = "Unit 1B", defaultMonthlyRent = 15000 }))
            .Content.ReadFromJsonAsync<IdResponse>();

        var res = await client.PutAsJsonAsync($"/api/units/{b!.Id}", new { name = "Unit 1A", defaultMonthlyRent = 15000 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("already has a unit with that name", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ending_a_lease_through_the_api_voids_rent_after_the_end_date()
    {
        var client = TrustedClient();
        var property = await (await client.PostAsJsonAsync("/api/properties", new { name = "Test Apartments" })).Content.ReadFromJsonAsync<IdResponse>();
        var unit = await (await client.PostAsJsonAsync($"/api/properties/{property!.Id}/units", new { name = "Unit 1A", defaultMonthlyRent = 12000 }))
            .Content.ReadFromJsonAsync<IdResponse>();
        var tenant = await (await client.PostAsJsonAsync("/api/tenants", new { fullName = "Juan Dela Cruz" })).Content.ReadFromJsonAsync<IdResponse>();
        // Starts a year ago so several periods are billed whatever today's date is.
        var start = DateOnly.FromDateTime(DateTime.Today).AddYears(-1);
        var lease = await (await client.PostAsJsonAsync("/api/leases", new
        {
            unitId = unit!.Id, tenantId = tenant!.Id, startDate = start, monthlyRent = 12000, dueDay = start.Day,
            gracePeriodDays = 0, securityDeposit = 0,
        })).Content.ReadFromJsonAsync<IdResponse>();

        var res = await client.PostAsJsonAsync($"/api/leases/{lease!.Id}/end", new { endDate = start.AddMonths(2).AddDays(3) });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        await using var db = NewContext();
        Assert.Equal(3, await db.Charges.CountAsync(c => c.LeaseId == lease.Id && !c.IsVoided));
    }

    private record IdResponse(int Id);
}
