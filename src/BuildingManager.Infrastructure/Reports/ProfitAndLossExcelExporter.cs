using BuildingManager.Core.Reports;
using BuildingManager.Infrastructure.Expenses;
using ClosedXML.Excel;
using static BuildingManager.Infrastructure.Reports.ExcelLayout;

namespace BuildingManager.Infrastructure.Reports;

public static class ProfitAndLossExcelExporter
{
    /// <summary>Sheet 1: the P&amp;L with live SUM formulas. Sheet 2: every expense in the period.</summary>
    public static byte[] Build(PnlReport report, IReadOnlyList<ExpenseRow> expenses, DateOnly from, DateOnly to, PnlBasis basis)
    {
        using var wb = new XLWorkbook();
        AddPnlSheet(wb, report, from, to, basis);
        AddExpenseSheet(wb, expenses, from, to);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void AddPnlSheet(XLWorkbook wb, PnlReport report, DateOnly from, DateOnly to, PnlBasis basis)
    {
        var ws = wb.Worksheets.Add("Profit & Loss");
        ws.Cell(1, 1).Value = "Profit and Loss";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"{from:dd MMM yyyy} – {to:dd MMM yyyy} · {basis} basis";
        ws.Cell(2, 1).Style.Font.SetItalic();

        var cols = report.Columns;
        var totalCol = cols.Count + 2;
        WriteHeader(ws, 4, ["", .. cols.Select(c => c.Label), "Total"]);

        var r = 5;
        int? Section(string title, PnlSection section, string totalLabel)
        {
            var lines = report.Lines.Where(l => l.Section == section).ToList();
            if (lines.Count == 0 && section == PnlSection.CapitalExpense) return null;
            ws.Cell(r, 1).Value = title;
            ws.Cell(r, 1).Style.Font.SetBold();
            r++;
            var first = r;
            foreach (var line in lines)
            {
                ws.Cell(r, 1).Value = "   " + line.Label;
                for (var i = 0; i < cols.Count; i++) ws.Cell(r, i + 2).Value = line.Amounts[cols[i].Key];
                ws.Cell(r, totalCol).FormulaA1 = $"SUM({Col(2)}{r}:{Col(totalCol - 1)}{r})";
                r++;
            }
            ws.Cell(r, 1).Value = totalLabel;
            for (var c = 2; c <= totalCol; c++)
                ws.Cell(r, c).FormulaA1 = lines.Count == 0 ? "0" : $"SUM({Col(c)}{first}:{Col(c)}{r - 1})";
            ws.Range(r, 1, r, totalCol).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
            var totalRow = r;
            r += 2;
            return totalRow;
        }

        var income = Section("Income", PnlSection.Income, "Total income")!.Value;
        var expenses = Section("Operating expenses", PnlSection.OperatingExpense, "Total operating expenses")!.Value;

        ws.Cell(r, 1).Value = "Net income";
        for (var c = 2; c <= totalCol; c++) ws.Cell(r, c).FormulaA1 = $"{Col(c)}{income}-{Col(c)}{expenses}";
        var net = ws.Range(r, 1, r, totalCol);
        net.Style.Font.SetBold().Font.SetFontSize(12).Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F1"));
        r += 2;

        if (Section("Capital expenses (not in net income)", PnlSection.CapitalExpense, "Total capital expenses") is not null)
            ws.Cell(r - 1, 1).Value = "Usually depreciated over several years; ask your accountant.";

        ws.Range(5, 2, r, totalCol).Style.NumberFormat.Format = PesoFormat;
        ws.Column(1).Width = 42;
        for (var c = 2; c <= totalCol; c++) ws.Column(c).Width = 16;
        ws.SheetView.FreezeRows(4);
    }

    private static void AddExpenseSheet(XLWorkbook wb, IReadOnlyList<ExpenseRow> expenses, DateOnly from, DateOnly to)
    {
        var ws = wb.Worksheets.Add("Expenses");
        ws.Cell(1, 1).Value = "Expenses";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"{from:dd MMM yyyy} – {to:dd MMM yyyy}";
        WriteHeader(ws, 4, ["Date", "Property", "Category", "Vendor", "Description", "Reference", "Method", "Amount", "Receipts"]);

        var r = 5;
        foreach (var e in expenses.OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            ws.Cell(r, 1).Value = e.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 2).Value = e.Property ?? "General";
            ws.Cell(r, 3).Value = e.Category;
            ws.Cell(r, 4).Value = e.Vendor;
            ws.Cell(r, 5).Value = e.Description;
            ws.Cell(r, 6).Value = e.Reference;
            ws.Cell(r, 7).Value = e.Method.ToString();
            ws.Cell(r, 8).Value = e.Amount;
            ws.Cell(r, 9).Value = e.Receipts.Count;
            r++;
        }
        WriteTotalsRow(ws, r, firstDataRow: 5, fromCol: 8, toCol: 8);
        ws.Range(5, 1, r, 1).Style.DateFormat.Format = DateFormat;
        ws.Range(5, 8, r, 8).Style.NumberFormat.Format = PesoFormat;
        Finish(ws, headerRow: 4);
    }

    private static string Col(int n) => XLHelper.GetColumnLetterFromNumber(n);
}
