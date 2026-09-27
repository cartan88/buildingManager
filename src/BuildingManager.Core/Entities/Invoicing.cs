namespace BuildingManager.Core.Entities;

/// <summary>The landlord's details printed on every statement. There is a single row (Id = 1).</summary>
public class BusinessProfile
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Tin { get; set; }
    /// <summary>Phone / email shown under the address.</summary>
    public string? Contact { get; set; }
    /// <summary>e.g. bank account or GCash number to pay to.</summary>
    public string? PaymentInstructions { get; set; }
    /// <summary>
    /// Kept as "Billing Statement" by default: only BIR-registered invoices may be called an invoice.
    /// </summary>
    public string DocumentTitle { get; set; } = "Billing Statement";
    public string NumberPrefix { get; set; } = "BS";
    public string? FooterNote { get; set; } = "This billing statement is not an official receipt or invoice.";
    /// <summary>Default days from issue date to due date for new statements.</summary>
    public int DefaultDueDays { get; set; } = 7;
}

public enum InvoiceStatus { Issued = 0, Voided = 1 }

/// <summary>
/// An issued billing statement. Everything printed on it is copied here when it's issued, and the exact
/// PDF is stored, so it never changes afterwards, even if tenant or business details are edited later.
/// It can only be voided, never edited or deleted.
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    public int LeaseId { get; set; }
    public Lease? Lease { get; set; }

    /// <summary>Numbering restarts each year: BS-2026-0001, BS-2026-0002, ...</summary>
    public int Year { get; set; }
    public int Sequence { get; set; }
    public required string Number { get; set; }

    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    // Snapshot of what was printed.
    public required string DocumentTitle { get; set; }
    public required string BusinessName { get; set; }
    public string? BusinessAddress { get; set; }
    public string? BusinessTin { get; set; }
    public string? BusinessContact { get; set; }
    public string? PaymentInstructions { get; set; }
    public string? FooterNote { get; set; }
    public required string TenantName { get; set; }
    public string? TenantTin { get; set; }
    public required string PropertyName { get; set; }
    public string? PropertyAddress { get; set; }
    public required string UnitName { get; set; }
    public string? Notes { get; set; }
    /// <summary>Amount due on the statement: the sum of the lines' balances.</summary>
    public decimal Total { get; set; }

    /// <summary>The PDF exactly as issued.</summary>
    public byte[] Pdf { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<InvoiceLine> Lines { get; set; } = [];
}

public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int ChargeId { get; set; }
    public Charge? Charge { get; set; }
    public int SortOrder { get; set; }
    public required string Description { get; set; }
    public DateOnly DueDate { get; set; }
    /// <summary>The charge's full amount.</summary>
    public decimal Amount { get; set; }
    /// <summary>Already paid on this charge when the statement was issued.</summary>
    public decimal Paid { get; set; }
    /// <summary>Still owed when the statement was issued.</summary>
    public decimal Balance { get; set; }
}
