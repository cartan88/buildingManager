using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Invoicing;

/// <summary>A rule the request broke; the API turns it into a 400 with the given field.</summary>
public class InvoiceValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <param name="ChargeIds">Charges to put on the statement; null means every unpaid charge on the lease.</param>
public record IssueInvoiceRequest(DateOnly IssueDate, DateOnly? DueDate = null, IReadOnlyCollection<int>? ChargeIds = null, string? Notes = null);

public enum InvoicePaymentStatus { Unpaid, PartiallyPaid, Paid, Voided }

public record InvoiceSummary(
    int Id, int LeaseId, string Number, DateOnly IssueDate, DateOnly DueDate, InvoiceStatus Status, string? VoidReason,
    string TenantName, string PropertyName, string UnitName, decimal Total, decimal StillOwed, InvoicePaymentStatus PaymentStatus);

public class InvoiceService(AppDbContext db)
{
    public async Task<BusinessProfile> GetProfileAsync(CancellationToken ct = default) =>
        await db.BusinessProfiles.FindAsync([BusinessProfile.SingletonId], ct) ?? new BusinessProfile();

    public async Task SaveProfileAsync(BusinessProfile input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvoiceValidationException("name", "Business or owner name is required.");
        if (string.IsNullOrWhiteSpace(input.DocumentTitle)) throw new InvoiceValidationException("documentTitle", "Document title is required.");
        if (string.IsNullOrWhiteSpace(input.NumberPrefix) || !input.NumberPrefix.Trim().All(char.IsLetterOrDigit))
            throw new InvoiceValidationException("numberPrefix", "Number prefix must be letters or digits, e.g. BS.");
        if (input.DefaultDueDays is < 0 or > 90) throw new InvoiceValidationException("defaultDueDays", "Due days must be 0–90.");

        var profile = await db.BusinessProfiles.FindAsync([BusinessProfile.SingletonId], ct);
        if (profile is null) db.BusinessProfiles.Add(profile = new BusinessProfile());
        profile.Name = input.Name.Trim();
        profile.Address = Clean(input.Address);
        profile.Tin = Clean(input.Tin);
        profile.Contact = Clean(input.Contact);
        profile.PaymentInstructions = Clean(input.PaymentInstructions);
        profile.DocumentTitle = input.DocumentTitle.Trim();
        profile.NumberPrefix = input.NumberPrefix.Trim().ToUpperInvariant();
        profile.FooterNote = Clean(input.FooterNote);
        profile.DefaultDueDays = input.DefaultDueDays;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Issues a numbered statement for the lease's unpaid charges. Numbers are sequential per year with no
    /// gaps, even when several statements are issued at once.
    /// </summary>
    public async Task<Invoice> IssueAsync(int leaseId, IssueInvoiceRequest request, CancellationToken ct = default)
    {
        var profile = await db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Name))
            throw new InvoiceValidationException("profile", "Fill in your business details under Settings before issuing statements.");

        var dueDate = request.DueDate ?? request.IssueDate.AddDays(profile.DefaultDueDays);
        if (dueDate < request.IssueDate) throw new InvoiceValidationException("dueDate", "Due date is before the issue date.");

        Invoice? invoice = null;
        // Lease lock first, then the numbering lock: always in this order, so batches can't deadlock.
        await db.InAppLockAsync(AppLocks.Lease(leaseId), async () =>
        {
            var lease = await db.Leases.AsNoTracking()
                .Where(l => l.Id == leaseId)
                .Select(l => new { l.Tenant!.FullName, l.Tenant.Tin, Property = l.Unit!.Property!.Name, l.Unit.Property.Address, Unit = l.Unit.Name })
                .FirstOrDefaultAsync(ct)
                ?? throw new InvoiceValidationException("leaseId", "Lease not found.");

            var open = await db.Charges.AsNoTracking()
                .Where(c => c.LeaseId == leaseId && !c.IsVoided)
                .Select(c => new { c.Id, c.Description, c.DueDate, c.Amount, Paid = c.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 })
                .Where(c => c.Amount > c.Paid)
                .OrderBy(c => c.DueDate).ThenBy(c => c.Id)
                .ToListAsync(ct);

            var chosen = request.ChargeIds is null ? open : open.Where(c => request.ChargeIds.Contains(c.Id)).ToList();
            if (request.ChargeIds is not null && chosen.Count != request.ChargeIds.Distinct().Count())
                throw new InvoiceValidationException("chargeIds", "Some selected charges are already paid, voided or belong to another lease.");
            if (chosen.Count == 0)
                throw new InvoiceValidationException("chargeIds", "There are no unpaid charges to put on a statement.");

            await db.InAppLockAsync(AppLocks.InvoiceNumbers, async () =>
            {
                var year = request.IssueDate.Year;
                var sequence = (await db.Invoices.Where(i => i.Year == year).MaxAsync(i => (int?)i.Sequence, ct) ?? 0) + 1;

                invoice = new Invoice
                {
                    LeaseId = leaseId,
                    Year = year,
                    Sequence = sequence,
                    Number = $"{profile.NumberPrefix}-{year}-{sequence:0000}",
                    IssueDate = request.IssueDate,
                    DueDate = dueDate,
                    DocumentTitle = profile.DocumentTitle,
                    BusinessName = profile.Name,
                    BusinessAddress = profile.Address,
                    BusinessTin = profile.Tin,
                    BusinessContact = profile.Contact,
                    PaymentInstructions = profile.PaymentInstructions,
                    FooterNote = profile.FooterNote,
                    TenantName = lease.FullName,
                    TenantTin = lease.Tin,
                    PropertyName = lease.Property,
                    PropertyAddress = lease.Address,
                    UnitName = lease.Unit,
                    Notes = Clean(request.Notes),
                    Lines = chosen.Select((c, i) => new InvoiceLine
                    {
                        ChargeId = c.Id, SortOrder = i, Description = c.Description, DueDate = c.DueDate,
                        Amount = c.Amount, Paid = c.Paid, Balance = c.Amount - c.Paid,
                    }).ToList(),
                };
                invoice.Total = invoice.Lines.Sum(l => l.Balance);
                invoice.Pdf = InvoicePdf.Render(invoice);

                db.Invoices.Add(invoice);
                await db.SaveChangesAsync(ct);
            }, ct);
        }, ct);
        return invoice!;
    }

    /// <summary>
    /// Issues a statement for every active lease with an unpaid charge that isn't on a live statement yet
    /// (so running it twice doesn't duplicate). Each statement includes all unpaid charges, arrears too.
    /// Each lease is its own transaction: if one fails, the statements already issued stay issued.
    /// </summary>
    public async Task<List<Invoice>> IssueForAllAsync(DateOnly issueDate, DateOnly? dueDate, CancellationToken ct = default)
    {
        var leaseIds = await db.Leases
            .Where(l => l.Status == LeaseStatus.Active && l.Charges.Any(c =>
                !c.IsVoided
                && c.Amount > (c.Allocations.Sum(a => (decimal?)a.Amount) ?? 0)
                && !db.InvoiceLines.Any(il => il.ChargeId == c.Id && il.Invoice!.Status == InvoiceStatus.Issued)))
            .OrderBy(l => l.Unit!.Property!.Name).ThenBy(l => l.Unit!.Name)
            .Select(l => l.Id)
            .ToListAsync(ct);

        var issued = new List<Invoice>();
        foreach (var id in leaseIds)
            issued.Add(await IssueAsync(id, new IssueInvoiceRequest(issueDate, dueDate), ct));
        return issued;
    }

    /// <summary>Voids a statement. Its number stays used and its original PDF is kept unchanged.</summary>
    public async Task VoidAsync(int invoiceId, string? reason, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvoiceValidationException("id", "Statement not found.");
        if (invoice.Status == InvoiceStatus.Voided) throw new InvoiceValidationException("id", "This statement is already void.");

        invoice.Status = InvoiceStatus.Voided;
        invoice.VoidedAt = DateTime.UtcNow;
        invoice.VoidReason = Clean(reason);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The PDF to hand out: the stored original, or for a voided statement the same document re-rendered
    /// from its snapshot with a VOID stamp, so a void copy can't be mistaken for a live one.
    /// </summary>
    public async Task<(string Number, byte[] Pdf)?> GetPdfAsync(int invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null) return null;
        return (invoice.Number, invoice.Status == InvoiceStatus.Voided ? InvoicePdf.Render(invoice) : invoice.Pdf);
    }

    /// <summary>Statements newest first, with how much of each is still unpaid right now.</summary>
    public async Task<List<InvoiceSummary>> ListAsync(int? leaseId = null, CancellationToken ct = default)
    {
        var rows = await db.Invoices.AsNoTracking()
            .Where(i => leaseId == null || i.LeaseId == leaseId)
            .OrderByDescending(i => i.Year).ThenByDescending(i => i.Sequence)
            .Select(i => new
            {
                i.Id, i.LeaseId, i.Number, i.IssueDate, i.DueDate, i.Status, i.VoidReason,
                i.TenantName, i.PropertyName, i.UnitName, i.Total,
                // A line is settled once its charge is paid or voided. Capped at the line's balance so
                // later charges or refunds can't make a statement look bigger than it was.
                Lines = i.Lines.Select(l => new
                {
                    l.Balance,
                    Owed = l.Charge!.IsVoided ? 0 : l.Charge.Amount - (l.Charge.Allocations.Sum(a => (decimal?)a.Amount) ?? 0),
                }).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var stillOwed = r.Status == InvoiceStatus.Voided ? 0 : r.Lines.Sum(l => Math.Clamp(l.Owed, 0, l.Balance));
            return new InvoiceSummary(r.Id, r.LeaseId, r.Number, r.IssueDate, r.DueDate, r.Status, r.VoidReason,
                r.TenantName, r.PropertyName, r.UnitName, r.Total, stillOwed, PaymentStatus(r.Status, r.Total, stillOwed));
        }).ToList();
    }

    /// <summary>How much of a statement is still unpaid, based on the current state of its charges.</summary>
    public static InvoicePaymentStatus PaymentStatus(InvoiceStatus status, decimal total, decimal stillOwed) =>
        status == InvoiceStatus.Voided ? InvoicePaymentStatus.Voided
        : stillOwed <= 0 ? InvoicePaymentStatus.Paid
        : stillOwed < total ? InvoicePaymentStatus.PartiallyPaid
        : InvoicePaymentStatus.Unpaid;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
