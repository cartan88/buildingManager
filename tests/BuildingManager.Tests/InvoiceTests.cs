using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Tests;

public class InvoiceTests : DatabaseTest
{
    private InvoiceService Invoices(Infrastructure.Data.AppDbContext db) => new(db);

    private async Task SetUpProfileAsync(string prefix = "BS")
    {
        await using var db = NewContext();
        await Invoices(db).SaveProfileAsync(new BusinessProfile { Name = "Sample Owner", Tin = "123-456-789-000", NumberPrefix = prefix, DefaultDueDays = 7 });
    }

    /// <summary>A lease billed Jun–Sep 2026 (4 × ₱12,000).</summary>
    private async Task<int> BilledLeaseAsync()
    {
        var leaseId = await SeedLeaseAsync();
        await using var db = NewContext();
        await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));
        return leaseId;
    }

    private async Task<Invoice> IssueAsync(int leaseId, string issueDate = "2026-09-27", IReadOnlyCollection<int>? chargeIds = null)
    {
        await using var db = NewContext();
        return await Invoices(db).IssueAsync(leaseId, new IssueInvoiceRequest(D(issueDate), ChargeIds: chargeIds));
    }

    [Fact]
    public async Task Business_details_are_required_before_issuing()
    {
        var leaseId = await BilledLeaseAsync();

        var e = await Assert.ThrowsAsync<InvoiceValidationException>(() => IssueAsync(leaseId));
        Assert.Equal("profile", e.Field);
    }

    [Fact]
    public async Task Statement_lists_every_unpaid_charge_with_paid_and_balance()
    {
        await SetUpProfileAsync();
        var leaseId = await BilledLeaseAsync();
        await PayAsync(leaseId, "2026-07-01", 30000); // Jun + Jul paid, Aug part-paid (6,000)

        var invoice = await IssueAsync(leaseId);

        Assert.Equal("BS-2026-0001", invoice.Number);
        Assert.Equal(D("2026-10-04"), invoice.DueDate); // default 7 days
        Assert.Equal(
            [("Rent – 15 Aug 2026 to 14 Sep 2026", 12000m, 6000m, 6000m), ("Rent – 15 Sep 2026 to 14 Oct 2026", 12000m, 0m, 12000m)],
            invoice.Lines.OrderBy(l => l.SortOrder).Select(l => (l.Description, l.Amount, l.Paid, l.Balance)));
        Assert.Equal(18000, invoice.Total);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(invoice.Pdf, 0, 4));
        Assert.Equal("Sample Owner", invoice.BusinessName);
        Assert.Equal("Juan Dela Cruz", invoice.TenantName);
    }

    [Fact]
    public async Task Selected_charges_only_and_rejects_ones_that_are_paid_or_from_another_lease()
    {
        await SetUpProfileAsync();
        var leaseId = await BilledLeaseAsync();
        var otherLease = await BilledLeaseAsync();
        await PayAsync(leaseId, "2026-07-01", 12000); // June paid

        await using var db = NewContext();
        var charges = await db.Charges.Where(c => c.LeaseId == leaseId).OrderBy(c => c.DueDate).Select(c => c.Id).ToListAsync();
        var foreign = await db.Charges.Where(c => c.LeaseId == otherLease).Select(c => c.Id).FirstAsync();

        var invoice = await IssueAsync(leaseId, chargeIds: [charges[3]]);
        Assert.Equal(12000, Assert.Single(invoice.Lines).Balance);

        await Assert.ThrowsAsync<InvoiceValidationException>(() => IssueAsync(leaseId, chargeIds: [charges[0]])); // already paid
        await Assert.ThrowsAsync<InvoiceValidationException>(() => IssueAsync(leaseId, chargeIds: [foreign]));
    }

    [Fact]
    public async Task Nothing_to_bill_is_an_error()
    {
        await SetUpProfileAsync();
        var leaseId = await BilledLeaseAsync();
        await PayAsync(leaseId, "2026-09-27", 48000);

        var e = await Assert.ThrowsAsync<InvoiceValidationException>(() => IssueAsync(leaseId));
        Assert.Contains("no unpaid charges", e.Message);
    }

    [Fact]
    public async Task Numbers_are_sequential_and_restart_each_year()
    {
        await SetUpProfileAsync();
        var leaseId = await BilledLeaseAsync();

        var numbers = new[]
        {
            (await IssueAsync(leaseId, "2026-09-27")).Number,
            (await IssueAsync(leaseId, "2026-12-31")).Number,
            (await IssueAsync(leaseId, "2027-01-02")).Number,
            (await IssueAsync(leaseId, "2027-01-03")).Number,
        };

        Assert.Equal(["BS-2026-0001", "BS-2026-0002", "BS-2027-0001", "BS-2027-0002"], numbers);
    }

    [Fact]
    public async Task Parallel_issuing_gives_unique_gap_free_numbers()
    {
        await SetUpProfileAsync();
        var leases = new List<int>();
        for (var i = 0; i < 6; i++) leases.Add(await BilledLeaseAsync());

        var issued = await Task.WhenAll(leases.Select(id => IssueAsync(id)));

        Assert.Equal(Enumerable.Range(1, 6).Select(n => $"BS-2026-{n:0000}"), issued.Select(i => i.Number).Order());
    }

    [Fact]
    public async Task Issued_statement_does_not_change_when_details_are_edited_later()
    {
        await SetUpProfileAsync();
        var leaseId = await BilledLeaseAsync();
        var invoice = await IssueAsync(leaseId);

        await using (var db = NewContext())
        {
            (await db.Tenants.SingleAsync()).FullName = "Juan P. Dela Cruz Jr.";
            await db.SaveChangesAsync();
            await Invoices(db).SaveProfileAsync(new BusinessProfile { Name = "New Name Holdings", DocumentTitle = "Invoice", NumberPrefix = "INV" });
        }

        await using var check = NewContext();
        var stored = await check.Invoices.SingleAsync();
        Assert.Equal("Juan Dela Cruz", stored.TenantName);
        Assert.Equal("Sample Owner", stored.BusinessName);
        Assert.Equal("Billing Statement", stored.DocumentTitle);
        Assert.Equal(invoice.Pdf, stored.Pdf);
        // The prefix change applies to the next statement only; the yearly sequence carries on.
        Assert.Equal("INV-2026-0002", (await IssueAsync(leaseId)).Number);
    }

    [Fact]
    public async Task Voiding_keeps_the_number_and_original_pdf_and_stamps_downloads()
    {
        await SetUpProfileAsync();
        var leaseId = await BilledLeaseAsync();
        var invoice = await IssueAsync(leaseId);

        await using (var db = NewContext()) await Invoices(db).VoidAsync(invoice.Id, "Wrong due date");

        await using var check = NewContext();
        var stored = await check.Invoices.SingleAsync();
        Assert.Equal(InvoiceStatus.Voided, stored.Status);
        Assert.Equal("Wrong due date", stored.VoidReason);
        Assert.Equal(invoice.Pdf, stored.Pdf); // original kept
        var download = await Invoices(check).GetPdfAsync(invoice.Id);
        Assert.NotEqual(invoice.Pdf, download!.Value.Pdf); // re-rendered with VOID stamp

        await Assert.ThrowsAsync<InvoiceValidationException>(() => Invoices(check).VoidAsync(invoice.Id, null));
        Assert.Equal("BS-2026-0002", (await IssueAsync(leaseId)).Number); // voided number is not reused
    }

    [Fact]
    public async Task Payment_status_follows_the_charges()
    {
        await SetUpProfileAsync();
        var leaseId = await SeedLeaseAsync(start: "2026-09-15");
        await using (var db = NewContext()) await Billing(db).GenerateRentChargesAsync(D("2026-09-27"));
        await IssueAsync(leaseId);

        async Task<InvoiceSummary> Summary() { await using var db = NewContext(); return (await Invoices(db).ListAsync(leaseId)).Single(); }

        Assert.Equal((InvoicePaymentStatus.Unpaid, 12000m), ((await Summary()).PaymentStatus, (await Summary()).StillOwed));
        await PayAsync(leaseId, "2026-09-28", 5000);
        Assert.Equal((InvoicePaymentStatus.PartiallyPaid, 7000m), ((await Summary()).PaymentStatus, (await Summary()).StillOwed));
        await PayAsync(leaseId, "2026-09-29", 7000);
        Assert.Equal((InvoicePaymentStatus.Paid, 0m), ((await Summary()).PaymentStatus, (await Summary()).StillOwed));
    }

    [Fact]
    public async Task Batch_skips_leases_already_fully_on_a_live_statement()
    {
        await SetUpProfileAsync();
        var billed = await BilledLeaseAsync();
        var alsoBilled = await BilledLeaseAsync();
        var paidUp = await BilledLeaseAsync();
        await PayAsync(paidUp, "2026-09-27", 48000);
        var already = await IssueAsync(billed);

        async Task<List<string>> Batch() { await using var db = NewContext(); return (await Invoices(db).IssueForAllAsync(D("2026-09-27"), null)).Select(i => i.Number).ToList(); }

        Assert.Equal(["BS-2026-0002"], await Batch()); // only alsoBilled
        Assert.Empty(await Batch()); // running it again doesn't duplicate

        await using (var db = NewContext()) await Invoices(db).VoidAsync(already.Id, null);
        Assert.Equal(["BS-2026-0003"], await Batch()); // voided statement no longer counts
        await using var check = NewContext();
        Assert.Equal(billed, (await check.Invoices.SingleAsync(i => i.Number == "BS-2026-0003")).LeaseId);
        Assert.Equal(alsoBilled, (await check.Invoices.SingleAsync(i => i.Number == "BS-2026-0002")).LeaseId);
    }
}
