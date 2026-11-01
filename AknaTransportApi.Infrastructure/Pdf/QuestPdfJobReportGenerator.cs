using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AknaTransportApi.Infrastructure.Pdf;

/// <summary>
/// "AKNA TRANSPORT — NAKLİYE İŞLERİ DÖKÜMÜ" raporu. A4 yatay, siyah-beyaz; mobil uygulamadaki dökümle
/// aynı düzen (siyah tablo başlığı, zebra satırlar, çerçeveli toplam kutusu). Başlık ve tablo başlığı her sayfada tekrarlanır; sayfa numarası ve oluşturulma tarihi alt bilgide yer alır.
/// </summary>
public sealed class QuestPdfJobReportGenerator(IBusinessCalendar calendar) : IJobReportPdfGenerator
{
    private const string Black = "#000000";
    private const string Muted = "#555555";
    private const string Faint = "#888888";
    private const string Rule = "#E0E0E0";
    private const string Zebra = "#F5F5F5";

    public byte[] Generate(JobReportDocument document)
    {
        var generatedLocal = calendar.ToLocal(document.GeneratedAtUtc);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginHorizontal(40);
                page.MarginTop(34);
                page.MarginBottom(22);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Black));

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
                row.RelativeItem().AlignBottom().Column(left =>
                {
                    left.Item().Text("AKNA TRANSPORT").FontSize(16).ExtraBold().LetterSpacing(0.15f);
                    left.Item().PaddingTop(3).Text(d.Title).FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.18f);
                    if (!string.IsNullOrWhiteSpace(d.CustomerName))
                        left.Item().PaddingTop(6).Text(t =>
                        {
                            t.Span("Müşteri: ").FontColor(Muted);
                            t.Span(d.CustomerName).SemiBold();
                        });
                    if (!string.IsNullOrWhiteSpace(d.Subtitle))
                        left.Item().PaddingTop(2).Text(d.Subtitle).FontColor(Muted);
                });

                row.AutoItem().AlignBottom().Column(c =>
                {
                    c.Item().Text("DÖNEM").FontSize(6.5f).SemiBold().FontColor(Faint).LetterSpacing(0.14f);
                    c.Item().PaddingTop(2).Text(PdfFormat.Period(d.StartDate, d.EndDate)).FontSize(8.5f).SemiBold();
                });
                row.ConstantItem(22);
                row.AutoItem().AlignBottom().Column(c =>
                {
                    c.Item().Text("İŞ SAYISI").FontSize(6.5f).SemiBold().FontColor(Faint).LetterSpacing(0.14f);
                    c.Item().PaddingTop(2).Text(d.Rows.Count.ToString(PdfFormat.Turkish)).FontSize(8.5f).SemiBold();
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
                    c.ConstantColumn(66);
                    c.ConstantColumn(118);
                    c.ConstantColumn(88);
                    c.RelativeColumn();
                    c.ConstantColumn(84);
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
                    table.Cell().ColumnSpan(5).PaddingVertical(20).AlignCenter()
                        .Text("Seçilen dönemde iş kaydı bulunmuyor.").FontColor(Faint);
                }

                for (var i = 0; i < d.Rows.Count; i++)
                {
                    var r = d.Rows[i];
                    var shade = i % 2 == 1 ? Zebra : "#FFFFFF";
                    BodyCell(table.Cell(), shade).Text(PdfFormat.Date(r.Date));
                    BodyCell(table.Cell(), shade).Text(r.StoreName).SemiBold();
                    BodyCell(table.Cell(), shade).Text(r.VehicleType.ToTurkish());
                    BodyCell(table.Cell(), shade).Text(r.Description);
                    BodyCell(table.Cell(), shade).AlignRight().Text(PdfFormat.Money(r.Price, d.Currency, decimals: false)).SemiBold();
                }
            });

            col.Item().PaddingTop(12).AlignRight().Width(225).Border(1).BorderColor(Black).Column(totals =>
            {
                totals.Item().PaddingVertical(7).PaddingHorizontal(9).Row(r =>
                {
                    r.RelativeItem().Text("Toplam iş sayısı").FontSize(8);
                    r.AutoItem().Text(d.Rows.Count.ToString(PdfFormat.Turkish)).FontSize(8).SemiBold();
                });
                totals.Item().Background(Black).PaddingVertical(7).PaddingHorizontal(9).Row(r =>
                {
                    r.RelativeItem().Text("Toplam nakliye tutarı").FontSize(9).FontColor(Colors.White);
                    r.AutoItem().Text(PdfFormat.Money(d.TotalAmount, d.Currency)).FontSize(9.5f).Bold().FontColor(Colors.White);
                });
            });
        });
    }

    private static void ComposeFooter(IContainer container, DateTime generatedLocal)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.75f).LineColor(Rule);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"Oluşturulma: {PdfFormat.DateTime(generatedLocal)}").FontSize(6.5f).FontColor(Faint);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(6.5f).FontColor(Faint));
                    t.Span("Sayfa ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) => c
        .Background(Black).PaddingVertical(6).PaddingHorizontal(7)
        .DefaultTextStyle(t => t.FontSize(6.5f).SemiBold().FontColor(Colors.White).LetterSpacing(0.12f));

    private static IContainer BodyCell(IContainer c, string background) => c
        .Background(background).BorderBottom(0.75f).BorderColor(Rule).PaddingVertical(6).PaddingHorizontal(7)
        .DefaultTextStyle(t => t.FontSize(8).LineHeight(1.3f));
}
