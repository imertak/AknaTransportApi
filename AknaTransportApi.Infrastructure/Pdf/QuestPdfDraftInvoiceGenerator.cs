using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AknaTransportApi.Infrastructure.Pdf;

/// <summary>
/// Fatura PDF'i. Taslakta her sayfada "TASLAK" filigranı ve "resmi fatura değildir" uyarısı bulunur.
/// <see cref="DraftInvoiceDocument.Official"/> ise (e-Arşiv'de kesilmiş, PDF vermeyen sağlayıcı) filigran yerine
/// GİB fatura numarası ve ETTN yazılır.
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
                if (!d.Official)
                    page.Background().AlignCenter().AlignMiddle().Rotate(-35)
                        .Text("TASLAK").FontSize(110).Bold().FontColor("#EEEEEE");

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(d.CompanyName.ToUpper(PdfFormat.Turkish)).FontSize(18).Bold().LetterSpacing(0.08f);
                            c.Item().Text(d.Official ? "e-ARŞİV FATURA" : "TASLAK FATURA").FontSize(11).SemiBold();
                            if (d.WithholdingLabel is not null) c.Item().Text("Fatura tipi: TEVKİFAT").FontSize(9);
                        });
                        row.ConstantItem(220).AlignRight().Column(c =>
                        {
                            if (d.Official)
                            {
                                c.Item().AlignRight().Text($"Fatura No: {d.EArchiveInvoiceNumber ?? d.InvoiceNumber}").SemiBold();
                                if (d.Ettn is not null) c.Item().AlignRight().Text($"ETTN: {d.Ettn}").FontSize(7.5f);
                            }
                            else
                            {
                                c.Item().AlignRight().Text($"Taslak No: {d.InvoiceNumber}").SemiBold();
                            }
                            c.Item().AlignRight().Text($"Tarih: {PdfFormat.Date(d.InvoiceDate)}");
                            if (!d.Official) c.Item().AlignRight().Text($"Durum: {d.Status}");
                        });
                    });
                    if (d.Official)
                        col.Item().PaddingTop(8).Border(1).Padding(6).AlignCenter()
                            .Text("e-Arşiv izni kapsamında elektronik ortamda iletilmiştir.").SemiBold();
                    else
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
                        if (d.WithholdingLabel is null)
                        {
                            Total(t, "Genel Toplam", PdfFormat.Money(d.GrandTotal, d.Currency), true);
                        }
                        else
                        {
                            Total(t, "Vergiler Dahil Toplam", PdfFormat.Money(d.GrandTotal, d.Currency), false);
                            Total(t, d.WithholdingLabel, "-" + PdfFormat.Money(d.WithholdingAmount, d.Currency), false);
                            Total(t, "Ödenecek Tutar", PdfFormat.Money(d.PayableAmount ?? d.GrandTotal, d.Currency), true);
                        }
                    });

                    // Açıklama: yazıyla tutar, IBAN ve not.
                    col.Item().PaddingTop(16).Border(0.5f).BorderColor("#BDBDBD").Padding(8).Column(n =>
                    {
                        n.Item().Text("AÇIKLAMA").FontSize(8).SemiBold().FontColor("#555555");
                        if (d.AmountInWords is not null) n.Item().PaddingTop(3).Text(d.AmountInWords).SemiBold();
                        if (d.Iban is not null)
                            n.Item().PaddingTop(3).Text(d.BankName is null ? $"IBAN: {d.Iban}" : $"IBAN: {d.Iban} ({d.BankName})");
                        if (!string.IsNullOrWhiteSpace(d.Notes)) n.Item().PaddingTop(3).Text(d.Notes);
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
