using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AknaTransportApi.Infrastructure.Pdf;

/// <summary>
/// Taslak fatura önizlemesi. Resmi e-Arşiv faturası değildir; her sayfada "TASLAK" filigranı ve
/// "Resmi fatura yerine geçmez" uyarısı bulunur.
/// </summary>
public sealed class QuestPdfDraftInvoiceGenerator(IBusinessCalendar calendar) : IDraftInvoicePdfGenerator
{
    public byte[] Generate(DraftInvoiceDocument d)
    {
        var generated = calendar.ToLocal(d.GeneratedAtUtc);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9.5f));
                page.Background().AlignCenter().AlignMiddle().Rotate(-35)
                    .Text("TASLAK").FontSize(110).Bold().FontColor("#EEEEEE");

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("AKNA TRANSPORT").FontSize(18).Bold().LetterSpacing(0.08f);
                            c.Item().Text("TASLAK FATURA").FontSize(11).SemiBold();
                        });
                        row.ConstantItem(200).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text($"Taslak No: {d.InvoiceNumber}").SemiBold();
                            c.Item().AlignRight().Text($"Tarih: {PdfFormat.Date(d.InvoiceDate)}");
                            c.Item().AlignRight().Text($"Durum: {d.Status}");
                        });
                    });
                    col.Item().PaddingTop(8).Border(1).Padding(6).AlignCenter()
                        .Text("BU BELGE RESMİ FATURA DEĞİLDİR. e-Arşiv faturası kesildiğinde geçerlilik kazanır.").SemiBold();
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Item().Text("SAYIN").FontSize(8).FontColor("#555555");
                    col.Item().Text(d.CustomerName).SemiBold();
                    if (!string.IsNullOrWhiteSpace(d.CustomerAddress)) col.Item().Text(d.CustomerAddress);
                    if (!string.IsNullOrWhiteSpace(d.CustomerTaxOffice) || !string.IsNullOrWhiteSpace(d.CustomerTaxNumber))
                        col.Item().Text($"Vergi Dairesi: {d.CustomerTaxOffice ?? "-"}   VKN/TCKN: {d.CustomerTaxNumber ?? "-"}");

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.ConstantColumn(45);
                            c.ConstantColumn(90);
                            c.ConstantColumn(90);
                        });
                        table.Header(h =>
                        {
                            Head(h.Cell()).Text("AÇIKLAMA");
                            Head(h.Cell()).AlignRight().Text("MİKTAR");
                            Head(h.Cell()).AlignRight().Text("BİRİM FİYAT");
                            Head(h.Cell()).AlignRight().Text("TUTAR");
                        });
                        foreach (var line in d.Lines)
                        {
                            Body(table.Cell()).Text(line.Description);
                            Body(table.Cell()).AlignRight().Text(line.Quantity.ToString("0.##", PdfFormat.Turkish));
                            Body(table.Cell()).AlignRight().Text(PdfFormat.Money(line.UnitPrice, d.Currency));
                            Body(table.Cell()).AlignRight().Text(PdfFormat.Money(line.LineTotal, d.Currency));
                        }
                    });

                    col.Item().PaddingTop(12).AlignRight().Width(260).Column(t =>
                    {
                        Total(t, "Ara Toplam", PdfFormat.Money(d.SubTotal, d.Currency), false);
                        Total(t, $"KDV (%{d.VatRate.ToString("0.##", PdfFormat.Turkish)})", PdfFormat.Money(d.VatAmount, d.Currency), false);
                        Total(t, "Genel Toplam", PdfFormat.Money(d.GrandTotal, d.Currency), true);
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Oluşturulma: {PdfFormat.DateTime(generated)}").FontSize(8).FontColor("#555555");
                    row.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8).FontColor("#555555"));
                        t.Span("Sayfa ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Total(ColumnDescriptor col, string label, string value, bool strong) =>
        col.Item().PaddingTop(3).Row(r =>
        {
            var l = r.RelativeItem().Text(label);
            var v = r.ConstantItem(120).AlignRight().Text(value);
            if (strong) { l.Bold(); v.Bold(); }
        });

    private static IContainer Head(IContainer c) => c.BorderBottom(1).PaddingVertical(5).PaddingHorizontal(3)
        .DefaultTextStyle(t => t.FontSize(8).SemiBold());

    private static IContainer Body(IContainer c) => c.BorderBottom(0.5f).BorderColor("#BDBDBD").PaddingVertical(5).PaddingHorizontal(3);
}
