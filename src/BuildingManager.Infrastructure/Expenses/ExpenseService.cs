using BuildingManager.Core.Entities;
using BuildingManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Expenses;

public class ExpenseValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <param name="VendorName">Typed on the form; matched to an existing vendor ignoring case, or created.</param>
public record ExpenseInput(DateOnly Date, int? PropertyId, int CategoryId, string? VendorName, string Description,
    decimal Amount, PaymentMethod Method, string? Reference, string? Notes);

public record ExpenseRow(int Id, DateOnly Date, int? PropertyId, string? Property, int CategoryId, string Category,
    ExpenseCategoryKind Kind, string? Vendor, string Description, decimal Amount, PaymentMethod Method, string? Reference,
    string? Notes, bool IsVoided, IReadOnlyList<ReceiptInfo> Receipts);

public record ReceiptInfo(int Id, string FileName, string ContentType, long SizeBytes);

public record ExpenseFilter(DateOnly? From = null, DateOnly? To = null, int? PropertyId = null, bool GeneralOnly = false,
    int? CategoryId = null, bool IncludeVoided = false);

public class ExpenseService(AppDbContext db)
{
    public const long MaxReceiptBytes = 10 * 1024 * 1024;

    /// <summary>Receipts are phone photos or scanned PDFs; anything else is refused.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedReceiptTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
    };

    public async Task<List<ExpenseRow>> ListAsync(ExpenseFilter filter, CancellationToken ct = default)
    {
        var q = db.Expenses.AsNoTracking().AsQueryable();
        if (!filter.IncludeVoided) q = q.Where(e => !e.IsVoided);
        if (filter.From is { } from) q = q.Where(e => e.Date >= from);
        if (filter.To is { } to) q = q.Where(e => e.Date <= to);
        if (filter.GeneralOnly) q = q.Where(e => e.PropertyId == null);
        else if (filter.PropertyId is { } pid) q = q.Where(e => e.PropertyId == pid);
        if (filter.CategoryId is { } cid) q = q.Where(e => e.CategoryId == cid);

        return await q
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new ExpenseRow(e.Id, e.Date, e.PropertyId, e.Property != null ? e.Property.Name : null,
                e.CategoryId, e.Category!.Name, e.Category.Kind, e.Vendor != null ? e.Vendor.Name : null,
                e.Description, e.Amount, e.Method, e.Reference, e.Notes, e.IsVoided,
                e.Receipts.OrderBy(r => r.Id).Select(r => new ReceiptInfo(r.Id, r.FileName, r.ContentType, r.SizeBytes)).ToList()))
            .ToListAsync(ct);
    }

    public async Task<int> CreateAsync(ExpenseInput input, CancellationToken ct = default)
    {
        var expense = new Expense { Description = "" };
        await ApplyAsync(expense, input, isNew: true, ct);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return expense.Id;
    }

    public async Task UpdateAsync(int id, ExpenseInput input, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FindAsync([id], ct) ?? throw new ExpenseValidationException("id", "Expense not found.");
        if (expense.IsVoided) throw new ExpenseValidationException("id", "A voided expense can't be edited.");
        await ApplyAsync(expense, input, isNew: false, ct);
        expense.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task VoidAsync(int id, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FindAsync([id], ct) ?? throw new ExpenseValidationException("id", "Expense not found.");
        expense.IsVoided = true;
        expense.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Expense expense, ExpenseInput input, bool isNew, CancellationToken ct)
    {
        if (input.Amount <= 0) throw new ExpenseValidationException("amount", "Amount must be more than zero.");
        if (string.IsNullOrWhiteSpace(input.Description)) throw new ExpenseValidationException("description", "Description is required.");
        if (input.PropertyId is { } pid && !await db.Properties.AnyAsync(p => p.Id == pid, ct))
            throw new ExpenseValidationException("propertyId", "Property not found.");

        var category = await db.ExpenseCategories.FindAsync([input.CategoryId], ct)
            ?? throw new ExpenseValidationException("categoryId", "Choose a category.");
        // Archived categories stay valid on existing expenses, but new ones can't use them.
        if (category.IsArchived && (isNew || expense.CategoryId != category.Id))
            throw new ExpenseValidationException("categoryId", "That category is archived.");

        expense.Date = input.Date;
        expense.PropertyId = input.PropertyId;
        expense.CategoryId = category.Id;
        expense.VendorId = await FindOrCreateVendorAsync(input.VendorName, ct);
        expense.Description = input.Description.Trim();
        expense.Amount = input.Amount;
        expense.Method = input.Method;
        expense.Reference = Clean(input.Reference);
        expense.Notes = Clean(input.Notes);
    }

    private async Task<int?> FindOrCreateVendorAsync(string? name, CancellationToken ct)
    {
        var trimmed = Clean(name);
        if (trimmed is null) return null;
        // The default SQL Server collation is case-insensitive, so this also matches "meralco" to "Meralco".
        var existing = await db.Vendors.Where(v => v.Name == trimmed).Select(v => (int?)v.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var vendor = new Vendor { Name = trimmed };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(ct);
        return vendor.Id;
    }

    public async Task<ReceiptInfo> AddReceiptAsync(int expenseId, string fileName, Stream content, long length, CancellationToken ct = default)
    {
        if (!await db.Expenses.AnyAsync(e => e.Id == expenseId, ct)) throw new ExpenseValidationException("id", "Expense not found.");
        if (length == 0) throw new ExpenseValidationException("file", "The file is empty.");
        if (length > MaxReceiptBytes) throw new ExpenseValidationException("file", "Receipts can be up to 10 MB.");
        var safeName = Path.GetFileName(fileName);
        if (!AllowedReceiptTypes.TryGetValue(Path.GetExtension(safeName), out var contentType))
            throw new ExpenseValidationException("file", "Upload a PDF or a photo (JPG, PNG, WEBP or HEIC).");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var receipt = new ExpenseReceipt
        {
            ExpenseId = expenseId, FileName = safeName, ContentType = contentType, SizeBytes = ms.Length, Content = ms.ToArray(),
        };
        db.ExpenseReceipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        return new ReceiptInfo(receipt.Id, receipt.FileName, receipt.ContentType, receipt.SizeBytes);
    }

    public Task<ExpenseReceipt?> GetReceiptAsync(int receiptId, CancellationToken ct = default) =>
        db.ExpenseReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receiptId, ct);

    /// <summary>A wrongly attached file can be removed; the expense record itself is never deleted.</summary>
    public async Task<bool> RemoveReceiptAsync(int receiptId, CancellationToken ct = default) =>
        await db.ExpenseReceipts.Where(r => r.Id == receiptId).ExecuteDeleteAsync(ct) > 0;

    public async Task<List<ExpenseCategory>> CategoriesAsync(CancellationToken ct = default) =>
        // Kind is stored as text ("Capital" < "Operating"), so order explicitly: operating first.
        await db.ExpenseCategories.AsNoTracking()
            .OrderBy(c => c.Kind == ExpenseCategoryKind.Capital ? 1 : 0).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<int> SaveCategoryAsync(int? id, string name, ExpenseCategoryKind kind, bool isArchived, CancellationToken ct = default)
    {
        var trimmed = Clean(name) ?? throw new ExpenseValidationException("name", "Category name is required.");
        if (await db.ExpenseCategories.AnyAsync(c => c.Name == trimmed && c.Id != id, ct))
            throw new ExpenseValidationException("name", "There's already a category with that name.");

        ExpenseCategory category;
        if (id is null)
        {
            var nextOrder = (await db.ExpenseCategories.MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 10;
            db.ExpenseCategories.Add(category = new ExpenseCategory { Name = trimmed, SortOrder = nextOrder });
        }
        else
        {
            category = await db.ExpenseCategories.FindAsync([id], ct) ?? throw new ExpenseValidationException("id", "Category not found.");
        }
        (category.Name, category.Kind, category.IsArchived) = (trimmed, kind, isArchived);
        await db.SaveChangesAsync(ct);
        return category.Id;
    }

    public async Task<List<string>> VendorNamesAsync(CancellationToken ct = default) =>
        await db.Vendors.AsNoTracking().OrderBy(v => v.Name).Select(v => v.Name).ToListAsync(ct);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
