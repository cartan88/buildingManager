namespace BuildingManager.Core.Reports;

public enum PnlSection { Income, OperatingExpense, CapitalExpense }

/// <summary>One amount going into the report, already tagged with the column it belongs to.</summary>
public record PnlEntry(string ColumnKey, PnlSection Section, string Line, decimal Amount);

public record PnlColumn(string Key, string Label);

public record PnlLine(PnlSection Section, string Label, IReadOnlyDictionary<string, decimal> Amounts, decimal Total);

public record PnlTotals(IReadOnlyDictionary<string, decimal> ByColumn, decimal Total);

public record PnlReport(
    IReadOnlyList<PnlColumn> Columns,
    IReadOnlyList<PnlLine> Lines,
    PnlTotals Income,
    PnlTotals OperatingExpenses,
    PnlTotals NetIncome,
    PnlTotals CapitalExpenses);

/// <summary>Lays out a profit and loss statement as a matrix of lines × columns (properties or months).</summary>
public static class ProfitAndLoss
{
    /// <param name="lineOrder">Order to show lines in; lines not listed come after, alphabetically.</param>
    public static PnlReport Build(IEnumerable<PnlEntry> entries, IReadOnlyList<PnlColumn> columns, IReadOnlyList<string> lineOrder)
    {
        var keys = columns.Select(c => c.Key).ToHashSet();
        var list = entries.Where(e => keys.Contains(e.ColumnKey) && e.Amount != 0).ToList();
        var ranks = lineOrder.Select((line, i) => (line, i)).DistinctBy(x => x.line).ToDictionary(x => x.line, x => x.i);
        int Rank(string line) => ranks.GetValueOrDefault(line, int.MaxValue);

        var lines = list
            .GroupBy(e => (e.Section, e.Line))
            .OrderBy(g => g.Key.Section).ThenBy(g => Rank(g.Key.Line)).ThenBy(g => g.Key.Line, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var amounts = columns.ToDictionary(c => c.Key, c => g.Where(e => e.ColumnKey == c.Key).Sum(e => e.Amount));
                return new PnlLine(g.Key.Section, g.Key.Line, amounts, amounts.Values.Sum());
            })
            .ToList();

        PnlTotals Sum(PnlSection section)
        {
            var byColumn = columns.ToDictionary(c => c.Key, c => lines.Where(l => l.Section == section).Sum(l => l.Amounts[c.Key]));
            return new PnlTotals(byColumn, byColumn.Values.Sum());
        }

        var income = Sum(PnlSection.Income);
        var operating = Sum(PnlSection.OperatingExpense);
        var net = columns.ToDictionary(c => c.Key, c => income.ByColumn[c.Key] - operating.ByColumn[c.Key]);
        return new PnlReport(columns, lines, income, operating, new PnlTotals(net, net.Values.Sum()), Sum(PnlSection.CapitalExpense));
    }

    /// <summary>One column per calendar month touched by the range, e.g. "2026-09" → "Sep 2026".</summary>
    public static List<PnlColumn> MonthColumns(DateOnly from, DateOnly to)
    {
        var columns = new List<PnlColumn>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
            columns.Add(new PnlColumn(MonthKey(m), m.ToString("MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-PH"))));
        return columns;
    }

    public static string MonthKey(DateOnly date) => $"{date.Year:0000}-{date.Month:00}";
}
