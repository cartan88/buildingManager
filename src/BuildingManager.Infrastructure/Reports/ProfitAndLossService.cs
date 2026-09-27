using BuildingManager.Core.Entities;
using BuildingManager.Core.Reports;
using BuildingManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BuildingManager.Infrastructure.Reports;

/// <summary>
/// Cash: income is money received in the period (by payment date), split by what it paid for.
/// Accrual: income is what was billed in the period (by charge due date), paid or not.
/// </summary>
public enum PnlBasis { Cash, Accrual }

public enum PnlGrouping { Property, Month }

public class ProfitAndLossService(AppDbContext db)
{
    public const string GeneralColumn = "general";
    public const string UnappliedLine = "Advance payments (not yet applied)";

    public static string IncomeLine(ChargeType type) => type switch
    {
        ChargeType.Rent => "Rent",
        ChargeType.Utility => "Utilities billed to tenants",
        ChargeType.LateFee => "Late fees",
        _ => "Other income",
    };

    private static readonly string[] IncomeOrder =
        [IncomeLine(ChargeType.Rent), IncomeLine(ChargeType.Utility), IncomeLine(ChargeType.LateFee), IncomeLine(ChargeType.Other), UnappliedLine];

    /// <param name="propertyId">Limit to one property (general expenses are then left out).</param>
    public async Task<PnlReport> BuildAsync(DateOnly from, DateOnly to, PnlBasis basis, PnlGrouping by, int? propertyId = null, CancellationToken ct = default)
    {
        var entries = new List<(DateOnly Date, int? PropertyId, PnlSection Section, string Line, decimal Amount)>();

        if (basis == PnlBasis.Cash)
        {
            var payments = await db.Payments.AsNoTracking()
                .Where(p => !p.IsVoided && p.PaymentDate >= from && p.PaymentDate <= to)
                .Where(p => propertyId == null || p.Lease!.Unit!.PropertyId == propertyId)
                .Select(p => new
                {
                    p.PaymentDate, p.Lease!.Unit!.PropertyId, p.Amount,
                    Applied = p.Allocations.Select(a => new { a.Amount, a.Charge!.Type }).ToList(),
                })
                .ToListAsync(ct);

            foreach (var p in payments)
            {
                foreach (var a in p.Applied) entries.Add((p.PaymentDate, p.PropertyId, PnlSection.Income, IncomeLine(a.Type), a.Amount));
                var unapplied = p.Amount - p.Applied.Sum(a => a.Amount);
                if (unapplied > 0) entries.Add((p.PaymentDate, p.PropertyId, PnlSection.Income, UnappliedLine, unapplied));
            }
        }
        else
        {
            var charges = await db.Charges.AsNoTracking()
                .Where(c => !c.IsVoided && c.DueDate >= from && c.DueDate <= to)
                .Where(c => propertyId == null || c.Lease!.Unit!.PropertyId == propertyId)
                .Select(c => new { c.DueDate, c.Lease!.Unit!.PropertyId, c.Type, c.Amount })
                .ToListAsync(ct);
            entries.AddRange(charges.Select(c => (c.DueDate, (int?)c.PropertyId, PnlSection.Income, IncomeLine(c.Type), c.Amount)));
        }

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => !e.IsVoided && e.Date >= from && e.Date <= to)
            .Where(e => propertyId == null || e.PropertyId == propertyId)
            .Select(e => new { e.Date, e.PropertyId, e.Category!.Kind, Category = e.Category.Name, e.Amount })
            .ToListAsync(ct);
        entries.AddRange(expenses.Select(e => (e.Date, e.PropertyId,
            e.Kind == ExpenseCategoryKind.Capital ? PnlSection.CapitalExpense : PnlSection.OperatingExpense, e.Category, e.Amount)));

        List<PnlColumn> columns;
        Func<(DateOnly Date, int? PropertyId, PnlSection, string, decimal), string> columnOf;
        if (by == PnlGrouping.Month)
        {
            columns = ProfitAndLoss.MonthColumns(from, to);
            columnOf = e => ProfitAndLoss.MonthKey(e.Date);
        }
        else
        {
            columns = await db.Properties.AsNoTracking()
                .Where(p => propertyId == null || p.Id == propertyId)
                .OrderBy(p => p.Name)
                .Select(p => new PnlColumn("p" + p.Id, p.Name))
                .ToListAsync(ct);
            if (entries.Any(e => e.PropertyId == null)) columns.Add(new PnlColumn(GeneralColumn, "General"));
            columnOf = e => e.PropertyId is { } id ? $"p{id}" : GeneralColumn;
        }

        var categoryOrder = await db.ExpenseCategories.AsNoTracking().OrderBy(c => c.SortOrder).Select(c => c.Name).ToListAsync(ct);
        return ProfitAndLoss.Build(
            entries.Select(e => new PnlEntry(columnOf(e), e.Section, e.Line, e.Amount)),
            columns,
            [.. IncomeOrder, .. categoryOrder]);
    }
}
