using ClosedXML.Excel;

namespace BuildingManager.Infrastructure.Reports;

/// <summary>Shared look for all Excel exports.</summary>
internal static class ExcelLayout
{
    public const string PesoFormat = "₱#,##0.00;[Red]-₱#,##0.00";
    public const string DateFormat = "dd-MMM-yyyy";

    public static void WriteTitle(IXLWorksheet ws, string title, DateOnly asOf)
    {
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"As of {asOf:dd MMMM yyyy}";
        ws.Cell(2, 1).Style.Font.SetItalic();
    }

    public static void WriteHeader(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) ws.Cell(row, i + 1).Value = headers[i];
        var range = ws.Range(row, 1, row, headers.Count);
        range.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F1"));
        range.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
    }

    public static void WriteTotalsRow(IXLWorksheet ws, int row, int firstDataRow, int fromCol, int toCol)
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

    public static void Finish(IXLWorksheet ws, int headerRow)
    {
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();
    }
}
