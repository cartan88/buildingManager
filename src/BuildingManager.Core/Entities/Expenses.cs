namespace BuildingManager.Core.Entities;

/// <summary>
/// Operating costs reduce net income. Capital items (renovations, appliances) are shown separately
/// and left out of net income, because they are normally depreciated over several years.
/// </summary>
public enum ExpenseCategoryKind { Operating = 0, Capital = 1 }

public class ExpenseCategory
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public ExpenseCategoryKind Kind { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Hidden from new expenses but kept for existing ones and reports.</summary>
    public bool IsArchived { get; set; }
}

public class Vendor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Contact { get; set; }
    public string? Tin { get; set; }
    public string? Notes { get; set; }
}

public class Expense
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Null for general costs not tied to one property (e.g. accountant's fees).</summary>
    public int? PropertyId { get; set; }
    public Property? Property { get; set; }
    public int CategoryId { get; set; }
    public ExpenseCategory? Category { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    /// <summary>OR / invoice no. from the vendor, check no., bank reference.</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    /// <summary>Expenses can be edited, but are voided rather than deleted.</summary>
    public bool IsVoided { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<ExpenseReceipt> Receipts { get; set; } = [];
}

/// <summary>A photo or PDF of a receipt, stored in the database so it's included in backups.</summary>
public class ExpenseReceipt
{
    public int Id { get; set; }
    public int ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
