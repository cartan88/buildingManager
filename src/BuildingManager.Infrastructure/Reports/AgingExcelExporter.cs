using BuildingManager.Core.Billing;
using ClosedXML.Excel;

namespace BuildingManager.Infrastructure.Reports;

public static class AgingExcelExporter
{
    public const string PesoFormat = "₱#,##0.00;[Red]-₱#,##0.00";

    private static readonly AgingBucket[] Buckets =
        [AgingBucket.Current, AgingBucket.Days1To30, AgingBucket.Days31To60, AgingBucket.Days61To90, AgingBucket.Over90];

    public static byte[] Build(IReadOnlyList<AgingRow> rows, DateOnly asOf)
    {
        using var wb = new XLWorkbook();
        AddSummarySheet(wb, rows, asOf);
        AddDetailSheet(wb, rows, asOf);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void AddSummarySheet(XLWorkbook wb, IReadOnlyList<AgingRow> rows, DateOnly asOf)
    {
        var ws = wb.Worksheets.Add("Summary");
        WriteTitle(ws, "Accounts Receivable Aging – Summary", asOf);

        string[] headers = ["Property", "Unit", "Tenant", "Phone", .. Buckets.Select(Aging.Label), "Total"];
        WriteHeader(ws, 4, headers);

        var r = 5;
        foreach (var g in rows.GroupBy(x => new { x.LeaseId, x.Property, x.Unit, x.Tenant, x.Phone })
                     .OrderBy(g => g.Key.Property).ThenBy(g => g.Key.Unit))
        {
            ws.Cell(r, 1).Value = g.Key.Property;
            ws.Cell(r, 2).Value = g.Key.Unit;
            ws.Cell(r, 3).Value = g.Key.Tenant;
            ws.Cell(r, 4).Value = g.Key.Phone;
            for (var i = 0; i < Buckets.Length; i++)
                ws.Cell(r, 5 + i).Value = g.Where(x => x.Bucket == Buckets[i]).Sum(x => x.Outstanding);
            ws.Cell(r, 10).FormulaA1 = $"SUM(E{r}:I{r})";
            r++;
        }

        WriteTotalsRow(ws, r, firstDataRow: 5, fromCol: 5, toCol: 10);
        ws.Range(5, 5, r, 10).Style.NumberFormat.Format = PesoFormat;
        Finish(ws, headerRow: 4);
    }

    private static void AddDetailSheet(XLWorkbook wb, IReadOnlyList<AgingRow> rows, DateOnly asOf)
    {
        var ws = wb.Worksheets.Add("Detail");
        WriteTitle(ws, "Accounts Receivable Aging – Detail", asOf);
        WriteHeader(ws, 4, ["Property", "Unit", "Tenant", "Description", "Due date", "Amount", "Paid", "Outstanding", "Days overdue", "Bucket"]);

        var r = 5;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.Property;
            ws.Cell(r, 2).Value = x.Unit;
            ws.Cell(r, 3).Value = x.Tenant;
            ws.Cell(r, 4).Value = x.Description;
            ws.Cell(r, 5).Value = x.DueDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 6).Value = x.Amount;
            ws.Cell(r, 7).Value = x.Paid;
            ws.Cell(r, 8).Value = x.Outstanding;
            ws.Cell(r, 9).Value = x.Bucket == AgingBucket.Current ? 0 : x.DaysOverdue;
            ws.Cell(r, 10).Value = x.BucketLabel;
            r++;
        }

        WriteTotalsRow(ws, r, firstDataRow: 5, fromCol: 6, toCol: 8);
        ws.Range(5, 5, r, 5).Style.DateFormat.Format = "dd-MMM-yyyy";
        ws.Range(5, 6, r, 8).Style.NumberFormat.Format = PesoFormat;
        Finish(ws, headerRow: 4);
    }

    private static void WriteTitle(IXLWorksheet ws, string title, DateOnly asOf)
    {
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"As of {asOf:dd MMMM yyyy}";
        ws.Cell(2, 1).Style.Font.SetItalic();
    }

    private static void WriteHeader(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) ws.Cell(row, i + 1).Value = headers[i];
        var range = ws.Range(row, 1, row, headers.Count);
        range.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F1"));
        range.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
    }

    private static void WriteTotalsRow(IXLWorksheet ws, int row, int firstDataRow, int fromCol, int toCol)
    {
        ws.Cell(row, 1).Value = "Total";
        for (var c = fromCol; c <= toCol; c++)
        {
            var col = XLHelper.GetColumnLetterFromNumber(c);
            ws.Cell(row, c).FormulaA1 = row > firstDataRow ? $"SUM({col}{firstDataRow}:{col}{row - 1})" : "0";
        }
        ws.Row(row).Style.Font.SetBold();
        ws.Range(row, 1, row, toCol).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
    }

    private static void Finish(IXLWorksheet ws, int headerRow)
    {
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();
    }
}
