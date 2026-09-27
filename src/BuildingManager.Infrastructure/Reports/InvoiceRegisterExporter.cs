using BuildingManager.Infrastructure.Invoicing;
using ClosedXML.Excel;
using static BuildingManager.Infrastructure.Reports.ExcelLayout;

namespace BuildingManager.Infrastructure.Reports;

/// <summary>Excel list of every statement issued, including voided ones, so the numbering can be audited.</summary>
public static class InvoiceRegisterExporter
{
    public static byte[] Build(IReadOnlyList<InvoiceSummary> invoices, DateOnly asOf)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Statements");
        WriteTitle(ws, "Billing Statement Register", asOf);
        WriteHeader(ws, 4, ["No.", "Issued", "Due", "Tenant", "Property", "Unit", "Amount", "Still owed", "Payment", "Status", "Void reason"]);

        var r = 5;
        foreach (var i in invoices.OrderBy(x => x.IssueDate).ThenBy(x => x.Number))
        {
            ws.Cell(r, 1).Value = i.Number;
            ws.Cell(r, 2).Value = i.IssueDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 3).Value = i.DueDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 4).Value = i.TenantName;
            ws.Cell(r, 5).Value = i.PropertyName;
            ws.Cell(r, 6).Value = i.UnitName;
            // Voided statements stay listed (numbers must be accounted for) but don't count in the totals.
            ws.Cell(r, 7).Value = i.Status == Core.Entities.InvoiceStatus.Voided ? 0 : i.Total;
            ws.Cell(r, 8).Value = i.StillOwed;
            ws.Cell(r, 9).Value = i.PaymentStatus switch
            {
                InvoicePaymentStatus.PartiallyPaid => "Partially paid",
                InvoicePaymentStatus.Voided => "—",
                var s => s.ToString(),
            };
            ws.Cell(r, 10).Value = i.Status.ToString();
            ws.Cell(r, 11).Value = i.VoidReason;
            if (i.Status == Core.Entities.InvoiceStatus.Voided) ws.Range(r, 1, r, 11).Style.Font.SetFontColor(XLColor.Gray);
            r++;
        }

        WriteTotalsRow(ws, r, firstDataRow: 5, fromCol: 7, toCol: 8);
        ws.Range(5, 2, r, 3).Style.DateFormat.Format = DateFormat;
        ws.Range(5, 7, r, 8).Style.NumberFormat.Format = PesoFormat;
        Finish(ws, headerRow: 4);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
