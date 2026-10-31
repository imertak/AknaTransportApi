using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AknaTransportApi.Infrastructure.Pdf;

/// <summary>
/// "AKNA TRANSPORT — NAKLİYE İŞLERİ DÖKÜMÜ" raporu. A4 yatay, siyah-beyaz, başlık ve tablo
/// başlığı her sayfada tekrarlanır; sayfa numarası ve oluşturulma tarihi alt bilgide yer alır.
/// </summary>
public sealed class QuestPdfJobReportGenerator(IBusinessCalendar calendar) : IJobReportPdfGenerator
{
    private const string Black = "#000000";
    private const string Muted = "#555555";
    private const string Rule = "#BDBDBD";

    public byte[] Generate(JobReportDocument document)
    {
        var generatedLocal = calendar.ToLocal(document.GeneratedAtUtc);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginHorizontal(36);
                page.MarginVertical(30);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Black));

                page.Header().Element(c => ComposeHeader(c, document));
                page.Content().PaddingTop(14).Element(c => ComposeTable(c, document));
                page.Footer().Element(c => ComposeFooter(c, generatedLocal));
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = "AKNA Transport - Nakliye İşleri Dökümü",
            Author = "AKNA Transport",
            Creator = "AKNA Transport API",
            Language = "tr-TR",
        })
        .GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, JobReportDocument d)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("AKNA TRANSPORT").FontSize(20).Bold().LetterSpacing(0.08f);
                    left.Item().PaddingTop(2).Text(d.Title).FontSize(11).SemiBold().LetterSpacing(0.06f);
                    if (!string.IsNullOrWhiteSpace(d.CustomerName))
                        left.Item().PaddingTop(6).Text(t =>
                        {
                            t.Span("Müşteri: ").FontColor(Muted);
                            t.Span(d.CustomerName).SemiBold();
                        });
                    if (!string.IsNullOrWhiteSpace(d.Subtitle))
                        left.Item().PaddingTop(2).Text(d.Subtitle).FontColor(Muted);
                });

                row.ConstantItem(170).Column(c =>
                {
                    c.Item().Text("DÖNEM").FontSize(7.5f).FontColor(Muted).LetterSpacing(0.08f);
                    c.Item().Text(PdfFormat.Period(d.StartDate, d.EndDate)).SemiBold();
                });
                row.ConstantItem(80).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text("İŞ SAYISI").FontSize(7.5f).FontColor(Muted).LetterSpacing(0.08f);
                    c.Item().AlignRight().Text(d.Rows.Count.ToString(PdfFormat.Turkish)).SemiBold();
                });
            });
            col.Item().PaddingTop(10).LineHorizontal(1.5f).LineColor(Black);
        });
    }

    private static void ComposeTable(IContainer container, JobReportDocument d)
    {
        container.Column(col =>
        {
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(78);
                    c.ConstantColumn(130);
                    c.ConstantColumn(100);
                    c.RelativeColumn();
                    c.ConstantColumn(105);
                });

                // Tablo başlığı her sayfada otomatik tekrarlanır.
                table.Header(h =>
                {
                    HeaderCell(h.Cell()).Text("TARİH");
                    HeaderCell(h.Cell()).Text("MAĞAZA / FİRMA");
                    HeaderCell(h.Cell()).Text("ARAÇ");
                    HeaderCell(h.Cell()).Text("YAPILAN NAKLİYELER");
                    HeaderCell(h.Cell()).AlignRight().Text("FİYAT");
                });

                if (d.Rows.Count == 0)
                {
                    table.Cell().ColumnSpan(5).PaddingVertical(16).AlignCenter()
                        .Text("Seçilen kriterlere uygun iş bulunamadı.").FontColor(Muted);
                }

                foreach (var r in d.Rows)
                {
                    BodyCell(table.Cell()).Text(PdfFormat.Date(r.Date));
                    BodyCell(table.Cell()).Text(r.StoreName);
                    BodyCell(table.Cell()).Text(r.VehicleType.ToTurkish());
                    BodyCell(table.Cell()).Text(r.Description);
                    BodyCell(table.Cell()).AlignRight().Text(PdfFormat.Money(r.Price, d.Currency));
                }
            });

            col.Item().PaddingTop(14).AlignRight().Width(300).Column(totals =>
            {
                totals.Item().BorderTop(1.5f).BorderColor(Black).PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text("Toplam iş sayısı").FontColor(Muted);
                    r.ConstantItem(120).AlignRight().Text(d.Rows.Count.ToString(PdfFormat.Turkish)).SemiBold();
                });
                totals.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text("Toplam nakliye tutarı").FontColor(Muted);
                    r.ConstantItem(120).AlignRight().Text(PdfFormat.Money(d.TotalAmount, d.Currency)).FontSize(11).Bold();
                });
            });
        });
    }

    private static void ComposeFooter(IContainer container, DateTime generatedLocal)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Rule);
            col.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text($"Oluşturulma: {PdfFormat.DateTime(generatedLocal)}").FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignCenter().Text("AKNA Transport · Nakliye ve Lojistik Hizmetleri").FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    t.Span("Sayfa ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) => c
        .BorderBottom(1).BorderColor(Black).PaddingVertical(6).PaddingHorizontal(4)
        .DefaultTextStyle(t => t.FontSize(8).SemiBold().LetterSpacing(0.06f));

    private static IContainer BodyCell(IContainer c) => c
        .BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(7).PaddingHorizontal(4);
}
