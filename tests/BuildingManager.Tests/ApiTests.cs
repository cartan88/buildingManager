using System.Net;
using System.Net.Http.Json;
using BuildingManager.Api;
using BuildingManager.Core.Entities;
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
            unitId = unit!.Id, tenantId = tenant!.Id, startDate = start, rent = 12000, dueDay = start.Day,
            gracePeriodDays = 0, securityDeposit = 0,
        })).Content.ReadFromJsonAsync<IdResponse>();

        var res = await client.PostAsJsonAsync($"/api/leases/{lease!.Id}/end", new { endDate = start.AddMonths(2).AddDays(3) });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        await using var db = NewContext();
        Assert.Equal(3, await db.Charges.CountAsync(c => c.LeaseId == lease.Id && !c.IsVoided));
    }

    [Fact]
    public async Task Statement_pdf_opens_inline_and_register_exports_to_excel()
    {
        var client = TrustedClient();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/settings/business", new
        {
            name = "Sample Owner", documentTitle = "Billing Statement", numberPrefix = "BS", defaultDueDays = 7,
        })).StatusCode);
        var property = await (await client.PostAsJsonAsync("/api/properties", new { name = "Test Apartments" })).Content.ReadFromJsonAsync<IdResponse>();
        var unit = await (await client.PostAsJsonAsync($"/api/properties/{property!.Id}/units", new { name = "Unit 1A", defaultMonthlyRent = 12000 }))
            .Content.ReadFromJsonAsync<IdResponse>();
        var tenant = await (await client.PostAsJsonAsync("/api/tenants", new { fullName = "Juan Dela Cruz" })).Content.ReadFromJsonAsync<IdResponse>();
        var start = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1);
        var lease = await (await client.PostAsJsonAsync("/api/leases", new
        {
            unitId = unit!.Id, tenantId = tenant!.Id, startDate = start, rent = 12000, dueDay = start.Day, gracePeriodDays = 0, securityDeposit = 0,
        })).Content.ReadFromJsonAsync<IdResponse>();

        var issued = await client.PostAsJsonAsync($"/api/leases/{lease!.Id}/invoices", new { issueDate = DateOnly.FromDateTime(DateTime.Today) });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var invoice = await issued.Content.ReadFromJsonAsync<IdResponse>();

        var pdf = await client.GetAsync($"/api/invoices/{invoice!.Id}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("inline", pdf.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains($"BS-{DateTime.Today.Year}-0001.pdf", pdf.Content.Headers.ContentDisposition?.ToString());

        var xlsx = await client.GetAsync("/api/invoices.xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await xlsx.Content.ReadAsByteArrayAsync())[..2])); // zip container

        var dup = await client.PostAsJsonAsync($"/api/leases/{lease.Id}/invoices", new { issueDate = DateOnly.FromDateTime(DateTime.Today), chargeIds = new[] { 999999 } });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
    }

    [Fact]
    public async Task Receipt_upload_and_download_round_trip_with_safe_headers()
    {
        var client = TrustedClient();
        var created = await client.PostAsJsonAsync("/api/expenses", new
        {
            date = "2026-09-10", propertyId = (int?)null, categoryId = 1, vendorName = "Ace Hardware", description = "Faucet",
            amount = 850, method = "Cash",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var expense = await created.Content.ReadFromJsonAsync<IdResponse>();

        using var form = new MultipartFormDataContent();
        var bytes = "%PDF-1.4 test"u8.ToArray();
        var part = new ByteArrayContent(bytes);
        // A name with quotes and non-ASCII characters, sent the RFC 5987 way.
        part.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data") { Name = "file", FileNameStar = "OR \"12\" ñ.pdf" };
        form.Add(part);
        var uploaded = await client.PostAsync($"/api/expenses/{expense!.Id}/receipts", form);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var receipt = await uploaded.Content.ReadFromJsonAsync<IdResponse>();

        var download = await _factory.CreateClient().GetAsync($"/api/receipts/{receipt!.Id}");
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("OR \"12\" ñ.pdf", download.Content.Headers.ContentDisposition?.FileNameStar);

        // Uploads are state-changing too: without the app's header they're refused.
        using var sneaky = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "x.pdf" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClient().PostAsync($"/api/expenses/{expense.Id}/receipts", sneaky)).StatusCode);
    }

    [Fact]
    public async Task Profit_and_loss_report_and_excel_export()
    {
        var client = TrustedClient();
        var json = await client.GetAsync("/api/reports/pnl?from=2026-01-01&to=2026-12-31&basis=Accrual&by=Month");
        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        Assert.Contains("\"Dec 2026\"", await json.Content.ReadAsStringAsync());

        var xlsx = await client.GetAsync("/api/reports/pnl.xlsx?from=2026-01-01&to=2026-12-31");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await xlsx.Content.ReadAsByteArrayAsync())[..2]));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/reports/pnl?from=2026-12-31&to=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task Tenant_can_be_marked_inactive_only_without_an_active_lease()
    {
        var client = TrustedClient();
        var property = await (await client.PostAsJsonAsync("/api/properties", new { name = "Test Apartments" })).Content.ReadFromJsonAsync<IdResponse>();
        var unit = await (await client.PostAsJsonAsync($"/api/properties/{property!.Id}/units", new { name = "Unit 1A", defaultMonthlyRent = 12000 }))
            .Content.ReadFromJsonAsync<IdResponse>();
        var tenant = await (await client.PostAsJsonAsync("/api/tenants", new { fullName = "Juan Dela Cruz" })).Content.ReadFromJsonAsync<IdResponse>();
        var start = DateOnly.FromDateTime(DateTime.Today);
        var leaseInput = new { unitId = unit!.Id, tenantId = tenant!.Id, startDate = start, rent = 12000, dueDay = start.Day, gracePeriodDays = 0, securityDeposit = 0 };
        var lease = await (await client.PostAsJsonAsync("/api/leases", leaseInput)).Content.ReadFromJsonAsync<IdResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/tenants/{tenant.Id}/deactivate", null)).StatusCode);

        await client.PostAsJsonAsync($"/api/leases/{lease!.Id}/end", new { endDate = start });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/tenants/{tenant.Id}/deactivate", null)).StatusCode);

        // Inactive tenants can't be given a new lease until reactivated, and history still blocks deletion.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/leases", leaseInput)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/tenants/{tenant.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/tenants/{tenant.Id}/activate", null)).StatusCode);
        await using var db = NewContext();
        Assert.True(await db.Tenants.Where(t => t.Id == tenant.Id).Select(t => t.IsActive).SingleAsync());
    }

    [Fact]
    public async Task Logo_upload_replace_and_remove_round_trip()
    {
        var client = TrustedClient();
        var png = TinyPng;

        Assert.Null((await client.GetFromJsonAsync<BrandingResponse>("/api/settings/branding"))!.LogoVersion);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/settings/logo")).StatusCode);

        // SVG can carry script, so only raster images are accepted.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/settings/logo", Upload("logo.svg", "<svg/>"u8.ToArray()))).StatusCode);
        // A file named .png that isn't really an image would break statement PDFs later.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/settings/logo", Upload("logo.png", [1, 2, 3]))).StatusCode);

        var uploaded = await client.PostAsync("/api/settings/logo", Upload("logo.png", png));
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var first = (await uploaded.Content.ReadFromJsonAsync<BrandingResponse>())!.LogoVersion;
        Assert.NotNull(first);

        var logo = await client.GetAsync("/api/settings/logo");
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await logo.Content.ReadAsByteArrayAsync());

        var replaced = await (await client.PostAsync("/api/settings/logo", Upload("new-logo.png", png)))
            .Content.ReadFromJsonAsync<BrandingResponse>();
        Assert.NotEqual(first, replaced!.LogoVersion); // new version so browsers don't show the cached old logo

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/settings/logo")).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<BrandingResponse>("/api/settings/branding"))!.LogoVersion);
    }

    [Fact]
    public async Task Statements_keep_the_logo_they_were_issued_with()
    {
        var client = TrustedClient();
        await client.PutAsJsonAsync("/api/settings/business", new { name = "Sample Owner", documentTitle = "Billing Statement", numberPrefix = "BS", defaultDueDays = 7 });
        var property = await (await client.PostAsJsonAsync("/api/properties", new { name = "Test Apartments" })).Content.ReadFromJsonAsync<IdResponse>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        async Task<int> NewLease(string unitName)
        {
            var unit = await (await client.PostAsJsonAsync($"/api/properties/{property!.Id}/units", new { name = unitName, defaultMonthlyRent = 12000 })).Content.ReadFromJsonAsync<IdResponse>();
            var tenant = await (await client.PostAsJsonAsync("/api/tenants", new { fullName = $"Tenant {unitName}" })).Content.ReadFromJsonAsync<IdResponse>();
            var start = today.AddMonths(-1);
            var lease = await (await client.PostAsJsonAsync("/api/leases", new
            {
                unitId = unit!.Id, tenantId = tenant!.Id, startDate = start, rent = 12000, dueDay = start.Day, gracePeriodDays = 0, securityDeposit = 0,
            })).Content.ReadFromJsonAsync<IdResponse>();
            return lease!.Id;
        }
        async Task<int> Issue(int leaseId) =>
            (await (await client.PostAsJsonAsync($"/api/leases/{leaseId}/invoices", new { issueDate = today })).Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var withoutLogo = await Issue(await NewLease("1A"));
        await client.PostAsync("/api/settings/logo", Upload("logo.png", TinyPng));
        var withLogo = await Issue(await NewLease("1B"));
        var sameLogo = await Issue(await NewLease("1C"));

        await using (var db = NewContext())
        {
            Assert.Null(await db.Invoices.Where(i => i.Id == withoutLogo).Select(i => i.LogoId).SingleAsync());
            Assert.NotNull(await db.Invoices.Where(i => i.Id == withLogo).Select(i => i.LogoId).SingleAsync());
            Assert.Equal(1, await db.StatementLogos.CountAsync()); // the same image is stored once, however many statements use it
            Assert.Equal(
                await db.Invoices.Where(i => i.Id == withLogo).Select(i => i.LogoId).SingleAsync(),
                await db.Invoices.Where(i => i.Id == sameLogo).Select(i => i.LogoId).SingleAsync());
        }

        // Removing the logo doesn't touch issued statements: a void copy is re-rendered with its original logo.
        await client.DeleteAsync("/api/settings/logo");
        await client.PostAsJsonAsync($"/api/invoices/{withLogo}/void", new { reason = "test" });
        var pdf = await client.GetAsync($"/api/invoices/{withLogo}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        await using (var db = NewContext())
            Assert.NotNull(await db.Invoices.Where(i => i.Id == withLogo).Select(i => i.LogoId).SingleAsync());
    }

    [Fact]
    public async Task Daily_lease_is_billed_every_day_at_the_daily_rate()
    {
        var client = TrustedClient();
        var property = await (await client.PostAsJsonAsync("/api/properties", new { name = "Bedspace House" })).Content.ReadFromJsonAsync<IdResponse>();
        var unit = await (await client.PostAsJsonAsync($"/api/properties/{property!.Id}/units", new { name = "Bed 1", defaultMonthlyRent = 6000 }))
            .Content.ReadFromJsonAsync<IdResponse>();
        var tenant = await (await client.PostAsJsonAsync("/api/tenants", new { fullName = "Juan Dela Cruz" })).Content.ReadFromJsonAsync<IdResponse>();
        var start = DateOnly.FromDateTime(DateTime.Today).AddDays(-4); // five days so far, today included
        var created = await client.PostAsJsonAsync("/api/leases", new
        {
            unitId = unit!.Id, tenantId = tenant!.Id, startDate = start, frequency = "Daily", rent = 500, dueDay = 1,
            gracePeriodDays = 0, securityDeposit = 0,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var lease = await created.Content.ReadFromJsonAsync<IdResponse>();

        await using (var db = NewContext())
        {
            var rent = await db.Charges.Where(c => c.LeaseId == lease!.Id && c.Type == ChargeType.Rent).OrderBy(c => c.DueDate).ToListAsync();
            Assert.Equal(5, rent.Count);
            Assert.All(rent, c => Assert.Equal(500m, c.Amount));
            Assert.Equal(start, rent[0].DueDate);
        }

        // The frequency is fixed: an edit keeps the lease daily and can't set a due day on it.
        var edit = await client.PutAsJsonAsync($"/api/leases/{lease!.Id}", new { rent = 550, dueDay = 20, gracePeriodDays = 0, securityDeposit = 0, frequency = "Monthly" });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        await using (var db = NewContext())
        {
            var saved = await db.Leases.SingleAsync(l => l.Id == lease.Id);
            Assert.Equal(RentFrequency.Daily, saved.Frequency);
            Assert.Equal(550m, saved.Rent);
            Assert.Equal(1, saved.DueDay);
        }
    }

    [Fact]
    public async Task Tenant_type_of_business_is_saved_trimmed_and_suggested()
    {
        var client = TrustedClient();
        var created = await (await client.PostAsJsonAsync("/api/tenants", new { fullName = "Aling Nena", businessType = "  Sari-sari store " }))
            .Content.ReadFromJsonAsync<IdResponse>();
        await client.PostAsJsonAsync("/api/tenants", new { fullName = "Mang Tomas", businessType = "Sari-sari store" });
        await client.PostAsJsonAsync("/api/tenants", new { fullName = "Juan Dela Cruz", businessType = "   " }); // blank = none

        var tenants = await client.GetFromJsonAsync<List<TenantRow>>("/api/tenants");
        Assert.Equal("Sari-sari store", tenants!.Single(t => t.Id == created!.Id).BusinessType);
        Assert.Null(tenants!.Single(t => t.FullName == "Juan Dela Cruz").BusinessType);
        Assert.Equal(["Sari-sari store"], await client.GetFromJsonAsync<List<string>>("/api/tenants/business-types"));

        var tooLong = await client.PutAsJsonAsync($"/api/tenants/{created!.Id}", new { fullName = "Aling Nena", businessType = new string('x', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    private record TenantRow(int Id, string FullName, string? BusinessType);

    /// <summary>A valid 1×1 PNG.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent Upload(string name, byte[] bytes) => new() { { new ByteArrayContent(bytes), "file", name } };

    private record IdResponse(int Id);
    private record BrandingResponse(string? Name, long? LogoVersion);
}
