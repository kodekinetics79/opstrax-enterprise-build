using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Zayra.Api.Infrastructure.Documents.Invoices;

public record InvoiceData(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string? PeriodDescription,
    decimal Amount,
    string CurrencyCode,
    string TenantName,
    string BillingEmail,
    string BillingCycle,
    string? Notes,
    IReadOnlyList<InvoiceLineItem> LineItems
);

public record InvoiceLineItem(string Description, decimal Amount);

public static class InvoicePdfService
{
    static InvoicePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(InvoiceData inv)
    {
        var currencySymbol = inv.CurrencyCode switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "AED" => "AED ",
            "SAR" => "SAR ",
            _     => inv.CurrencyCode + " "
        };

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Darken3));

                // ── Header ────────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // Left: branding
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("KynexOne Workforce").FontSize(18).Bold().FontColor("#1e3a5f");
                            c.Item().Text("Workforce Intelligence Platform").FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                        // Right: invoice label
                        row.ConstantItem(140).AlignRight().Column(c =>
                        {
                            c.Item().Background("#1e3a5f").Padding(8).AlignCenter()
                                .Text("TAX INVOICE").FontSize(13).Bold().FontColor(Colors.White);
                            c.Item().PaddingTop(4).Text($"# {inv.InvoiceNumber}").FontSize(10).SemiBold().AlignRight();
                        });
                    });
                    col.Item().PaddingTop(10).LineHorizontal(2).LineColor("#1e3a5f");
                });

                // ── Body ──────────────────────────────────────────────────────
                page.Content().PaddingTop(20).Column(col =>
                {
                    // Bill To + Invoice Details side by side
                    col.Item().Row(row =>
                    {
                        // Bill To
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BILL TO").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
                            c.Item().PaddingTop(4).Text(inv.TenantName).FontSize(12).Bold().FontColor("#1e3a5f");
                            c.Item().Text(inv.BillingEmail).FontSize(9).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrWhiteSpace(inv.BillingCycle))
                                c.Item().Text($"Billing cycle: {inv.BillingCycle}").FontSize(8).FontColor(Colors.Grey.Medium);
                        });

                        row.ConstantItem(20);

                        // Invoice meta
                        row.ConstantItem(200).Background("#f1f5f9").Padding(10).Column(c =>
                        {
                            MetaRow(c, "Invoice Date", inv.InvoiceDate.ToString("dd MMM yyyy"));
                            MetaRow(c, "Due Date",     inv.DueDate.ToString("dd MMM yyyy"));
                            if (!string.IsNullOrWhiteSpace(inv.PeriodDescription))
                                MetaRow(c, "Period", inv.PeriodDescription);
                        });
                    });

                    col.Item().PaddingVertical(16).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

                    // Line items table header
                    col.Item().Background("#1e3a5f").Padding(8).Row(row =>
                    {
                        row.RelativeItem().Text("DESCRIPTION").FontSize(8).Bold().FontColor(Colors.White);
                        row.ConstantItem(100).AlignRight().Text("AMOUNT").FontSize(8).Bold().FontColor(Colors.White);
                    });

                    // Line items
                    var isEven = false;
                    foreach (var item in inv.LineItems)
                    {
                        isEven = !isEven;
                        col.Item().Background(isEven ? "#f8fafc" : Colors.White).Padding(8).Row(row =>
                        {
                            row.RelativeItem().Text(item.Description).FontSize(9);
                            row.ConstantItem(100).AlignRight()
                                .Text($"{currencySymbol}{item.Amount:N2}").FontSize(9);
                        });
                    }

                    // Subtotal / total
                    var total = inv.LineItems.Sum(l => l.Amount);
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(200).Column(c =>
                        {
                            c.Item().Padding(8).Row(r =>
                            {
                                r.RelativeItem().Text("SUBTOTAL").FontSize(9).SemiBold();
                                r.ConstantItem(90).AlignRight().Text($"{currencySymbol}{total:N2}").FontSize(9);
                            });
                            c.Item().Background("#1e3a5f").Padding(10).Row(r =>
                            {
                                r.RelativeItem().Text("TOTAL DUE").FontSize(11).Bold().FontColor(Colors.White);
                                r.ConstantItem(90).AlignRight()
                                    .Text($"{currencySymbol}{total:N2}").FontSize(11).Bold().FontColor("#60a5fa");
                            });
                        });
                    });

                    // Notes / payment instructions
                    col.Item().PaddingTop(24).Column(c =>
                    {
                        c.Item().Text("PAYMENT INSTRUCTIONS").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
                        c.Item().PaddingTop(4).Text(
                            "Please arrange bank transfer or use the payment method agreed with your account manager. " +
                            "Quote the invoice number in the payment reference. " +
                            "Payment is due by the date shown above."
                        ).FontSize(8).FontColor(Colors.Grey.Darken2);

                        if (!string.IsNullOrWhiteSpace(inv.Notes))
                        {
                            c.Item().PaddingTop(10).Text("NOTES").FontSize(8).Bold().FontColor(Colors.Grey.Medium);
                            c.Item().PaddingTop(4).Text(inv.Notes).FontSize(8).FontColor(Colors.Grey.Darken2);
                        }
                    });
                });

                // ── Footer ────────────────────────────────────────────────────
                page.Footer().BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("KynexOne Workforce · This is a system-generated invoice.")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                    row.ConstantItem(160).AlignRight()
                        .Text($"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static void MetaRow(ColumnDescriptor c, string label, string value)
    {
        c.Item().PaddingBottom(4).Row(r =>
        {
            r.ConstantItem(80).Text(label + ":").FontSize(8).FontColor(Colors.Grey.Medium);
            r.RelativeItem().Text(value).FontSize(8).SemiBold();
        });
    }
}
