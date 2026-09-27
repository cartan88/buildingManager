using System.Globalization;
using BuildingManager.Core.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BuildingManager.Infrastructure.Invoicing;

/// <summary>Renders a statement to PDF using only the invoice's own snapshot fields.</summary>
public static class InvoicePdf
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");
    private const string Accent = "#1F5FBF";

    static InvoicePdf()
    {
        // Free for individuals and businesses under USD 1M annual revenue.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string Peso(decimal amount) => amount.ToString("C", Ph);
    private static string Date(DateOnly d) => d.ToString("dd MMM yyyy", Ph);

    public static byte[] Render(Invoice inv) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(40);
        // Lato ships inside QuestPDF, so a statement renders identically on any PC (system fonts aren't used).
        page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Lato").FontColor("#1D2330"));

        if (inv.Status == InvoiceStatus.Voided)
            page.Foreground().AlignCenter().AlignMiddle().Rotate(-30)
                .Text("VOID").FontSize(140).Bold().FontColor(Colors.Red.Lighten3);

        page.Header().Element(c => Header(c, inv));
        page.Content().PaddingTop(20).Element(c => Content(c, inv));
        page.Footer().Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(inv.FooterNote))
                col.Item().AlignCenter().Text(inv.FooterNote).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
            col.Item().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span($"{inv.Number} · Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    })).GeneratePdf();

    private static void Header(IContainer container, Invoice inv) => container.Row(row =>
    {
        row.RelativeItem().Column(col =>
        {
            // Statements issued without a logo just show the name.
            if (inv.Logo is { } logo)
                col.Item().PaddingBottom(6).Height(56).MaxWidth(200).AlignLeft().Image(logo.Content).FitArea();
            col.Item().Text(inv.BusinessName).FontSize(16).Bold();
            if (!string.IsNullOrWhiteSpace(inv.BusinessAddress)) col.Item().Text(inv.BusinessAddress);
            if (!string.IsNullOrWhiteSpace(inv.BusinessTin)) col.Item().Text($"TIN: {inv.BusinessTin}");
            if (!string.IsNullOrWhiteSpace(inv.BusinessContact)) col.Item().Text(inv.BusinessContact);
        });

        row.ConstantItem(200).Column(col =>
        {
            col.Item().AlignRight().Text(inv.DocumentTitle.ToUpper(Ph)).FontSize(18).Bold().FontColor(Accent);
            col.Item().PaddingTop(6).AlignRight().Text(t => { t.Span("No. ").FontColor(Colors.Grey.Darken1); t.Span(inv.Number).Bold(); });
            col.Item().AlignRight().Text(t => { t.Span("Date: ").FontColor(Colors.Grey.Darken1); t.Span(Date(inv.IssueDate)); });
            col.Item().AlignRight().Text(t => { t.Span("Due: ").FontColor(Colors.Grey.Darken1); t.Span(Date(inv.DueDate)).Bold(); });
        });
    });

    private static void Content(IContainer container, Invoice inv) => container.Column(col =>
    {
        col.Spacing(16);

        col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(bill =>
        {
            bill.Item().Text("BILL TO").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
            bill.Item().Text(inv.TenantName).FontSize(12).Bold();
            if (!string.IsNullOrWhiteSpace(inv.TenantTin)) bill.Item().Text($"TIN: {inv.TenantTin}");
            bill.Item().Text($"{inv.UnitName}, {inv.PropertyName}");
            if (!string.IsNullOrWhiteSpace(inv.PropertyAddress)) bill.Item().Text(inv.PropertyAddress);
        });

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(1.6f);
                c.RelativeColumn(1.6f);
                c.RelativeColumn(1.6f);
                c.RelativeColumn(1.6f);
            });

            table.Header(h =>
            {
                h.Cell().Element(HeadCell).Text("Description");
                h.Cell().Element(HeadCell).Text("Due date");
                h.Cell().Element(HeadCell).AlignRight().Text("Amount");
                h.Cell().Element(HeadCell).AlignRight().Text("Paid");
                h.Cell().Element(HeadCell).AlignRight().Text("Balance");
            });

            foreach (var line in inv.Lines.OrderBy(l => l.SortOrder))
            {
                table.Cell().Element(BodyCell).Text(line.Description);
                table.Cell().Element(BodyCell).Text(Date(line.DueDate));
                table.Cell().Element(BodyCell).AlignRight().Text(Peso(line.Amount));
                table.Cell().Element(BodyCell).AlignRight().Text(line.Paid == 0 ? "—" : Peso(line.Paid));
                table.Cell().Element(BodyCell).AlignRight().Text(Peso(line.Balance));
            }
        });

        col.Item().AlignRight().Width(230).Background(Accent).Padding(10).Row(r =>
        {
            r.RelativeItem().Text("AMOUNT DUE").FontColor(Colors.White).Bold();
            r.AutoItem().Text(Peso(inv.Total)).FontColor(Colors.White).FontSize(13).Bold();
        });

        if (!string.IsNullOrWhiteSpace(inv.PaymentInstructions))
            col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(p =>
            {
                p.Item().Text("HOW TO PAY").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                p.Item().Text(inv.PaymentInstructions);
            });

        if (!string.IsNullOrWhiteSpace(inv.Notes))
            col.Item().Column(n =>
            {
                n.Item().Text("NOTES").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                n.Item().Text(inv.Notes);
            });
    });

    private static IContainer HeadCell(IContainer c) =>
        c.BorderBottom(1.5f).BorderColor(Accent).PaddingVertical(5).PaddingHorizontal(4).DefaultTextStyle(t => t.Bold().FontSize(9));

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(4);
}
