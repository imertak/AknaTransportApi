using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AknaTransportApi.Infrastructure.Pdf;

/// <summary>
/// Kurumsal nakliye teklifi PDF'i. A4 dikey, siyah-beyaz; AKNA logosu, firma bilgileri, müşteri ve taşıma özeti,
/// fiyatlandırma tablosu, toplamlar, koşullar ve müşteri onayı için imza alanı. Türkçe karakterler QuestPDF'in
/// gömülü Lato fontuyla eksiksiz basılır. Tablo başlığı sayfa taşmalarında tekrarlanır.
/// </summary>
public sealed class QuestPdfTransportOfferGenerator(IBusinessCalendar calendar) : ITransportOfferPdfGenerator
{
    private const string Black = "#000000";
    private const string Muted = "#555555";
    private const string Faint = "#8A8A8A";
    private const string Rule = "#E0E0E0";
    private const string Panel = "#F6F6F6";

    public byte[] Generate(TransportOfferDocument d)
    {
        var generated = calendar.ToLocal(d.GeneratedAtUtc);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(40);
                page.MarginTop(36);
                page.MarginBottom(26);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Black).LineHeight(1.25f));

                if (d.ShowStatus)
                    page.Background().AlignCenter().AlignMiddle().Rotate(-35)
                        .Text(d.StatusLabel).FontSize(80).Bold().FontColor("#EFEFEF");

                page.Header().Element(c => ComposeHeader(c, d));
                page.Content().PaddingTop(12).Element(c => ComposeContent(c, d));
                page.Footer().Element(c => ComposeFooter(c, d, generated));
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = $"Nakliye Teklifi {d.OfferNumber}",
            Subject = $"{d.CustomerName} - Nakliye Teklifi",
            Author = d.CompanyName,
            Creator = "AKNA Transport API",
            Language = "tr-TR",
        })
        .GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, TransportOfferDocument d)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Row(r => r.AutoItem().Element(c => PdfBrand.Logo(c, 88)));
                    left.Item().PaddingTop(8).Text(d.CompanyName.ToUpper(PdfFormat.Turkish)).FontSize(10).Bold().LetterSpacing(0.08f);
                    if (d.CompanyAddress is not null) left.Item().Text(d.CompanyAddress).FontSize(8).FontColor(Muted);
                    var contact = Join("  ·  ", d.CompanyPhone, d.CompanyEmail, d.CompanyWebsite);
                    if (contact is not null) left.Item().Text(contact).FontSize(8).FontColor(Muted);
                    var tax = d.CompanyTaxOffice is null && d.CompanyTaxNumber is null
                        ? null
                        : $"Vergi Dairesi: {d.CompanyTaxOffice ?? "-"}   VKN: {d.CompanyTaxNumber ?? "-"}";
                    if (tax is not null) left.Item().Text(tax).FontSize(8).FontColor(Muted);
                });

                row.ConstantItem(200).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("NAKLİYE TEKLİFİ").FontSize(18).Bold().LetterSpacing(0.04f);
                    right.Item().PaddingTop(8).Border(1).BorderColor(Black).Column(meta =>
                    {
                        MetaRow(meta, "Teklif No", d.OfferNumber, strong: true, first: true);
                        MetaRow(meta, "Teklif Tarihi", PdfFormat.Date(d.OfferDate));
                        MetaRow(meta, "Geçerlilik", PdfFormat.Date(d.ValidUntil));
                    });
                    if (d.ShowStatus)
                        right.Item().PaddingTop(6).AlignRight().Background(Black).PaddingHorizontal(8).PaddingVertical(3)
                            .Text(d.StatusLabel).FontSize(8).Bold().FontColor(Colors.White).LetterSpacing(0.1f);
                });
            });
            col.Item().PaddingTop(12).LineHorizontal(1.5f).LineColor(Black);
        });
    }

    private static void MetaRow(ColumnDescriptor col, string label, string value, bool strong = false, bool first = false)
    {
        col.Item().Element(c => first ? c : c.BorderTop(0.5f).BorderColor(Rule)).PaddingHorizontal(8).PaddingVertical(4).Row(r =>
        {
            r.RelativeItem().Text(label).FontSize(8).FontColor(Muted);
            var v = r.AutoItem().Text(value).FontSize(strong ? 9.5f : 9);
            if (strong) v.Bold(); else v.SemiBold();
        });
    }

    private static void ComposeContent(IContainer container, TransportOfferDocument d)
    {
        container.Column(col =>
        {
            col.Spacing(11);

            // Müşteri ve taşıma özeti
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => InfoPanel(c, "MÜŞTERİ BİLGİLERİ", p =>
                {
                    p.Item().Text(d.CustomerName).FontSize(11).Bold();
                    Field(p, "Yetkili", d.ContactPerson);
                    Field(p, "Telefon", d.CustomerPhone);
                    Field(p, "E-posta", d.CustomerEmail);
                    Field(p, "Adres", d.CustomerAddress);
                }));
                row.ConstantItem(14);
                row.RelativeItem().Element(c => InfoPanel(c, "TAŞIMA BİLGİLERİ", p =>
                {
                    Field(p, "Taşıma türü", d.TransportTypeLabel, always: true);
                    Field(p, "Araç tipi", d.VehicleTypeName, always: true);
                    Field(p, "Yükleme tarihi", d.LoadingDate is { } l ? PdfFormat.Date(l) : null);
                    Field(p, "Teslimat tarihi", d.DeliveryDate is { } t ? PdfFormat.Date(t) : null);
                    Field(p, "Mesafe", d.DistanceKm is { } km ? $"{km.ToString("#,##0.#", PdfFormat.Turkish)} km" : null);
                }));
            });

            // Güzergâh
            col.Item().Border(1).BorderColor(Black).Row(row =>
            {
                row.RelativeItem().PaddingHorizontal(10).PaddingVertical(8).Column(c => RouteEnd(c, "YÜKLEME NOKTASI", d.LoadingPoint, d.LoadingAddress));
                row.ConstantItem(36).Background(Black).AlignCenter().AlignMiddle().Text("→").FontSize(16).Bold().FontColor(Colors.White);
                row.RelativeItem().PaddingHorizontal(10).PaddingVertical(8).Column(c => RouteEnd(c, "BOŞALTMA NOKTASI", d.UnloadingPoint, d.UnloadingAddress));
            });

            if (!string.IsNullOrWhiteSpace(d.CargoDescription))
                col.Item().Column(c =>
                {
                    c.Item().Text("YÜK AÇIKLAMASI").FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.12f);
                    c.Item().PaddingTop(3).Text(d.CargoDescription);
                });

            // Fiyatlandırma
            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(5).Text("FİYATLANDIRMA").FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.12f);
                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(24);
                        cd.RelativeColumn();
                        cd.ConstantColumn(70);
                        cd.ConstantColumn(85);
                        cd.ConstantColumn(90);
                    });
                    table.Header(h =>
                    {
                        Head(h.Cell()).Text("#");
                        Head(h.Cell()).Text("AÇIKLAMA");
                        Head(h.Cell()).AlignRight().Text("MİKTAR");
                        Head(h.Cell()).AlignRight().Text("BİRİM FİYAT");
                        Head(h.Cell()).AlignRight().Text("TUTAR");
                    });
                    foreach (var item in d.Items)
                    {
                        Body(table.Cell()).Text(item.Sequence.ToString(PdfFormat.Turkish)).FontColor(Faint);
                        Body(table.Cell()).Text(item.Description);
                        Body(table.Cell()).AlignRight().Text(Quantity(item.Quantity, item.Unit));
                        Body(table.Cell()).AlignRight().Text(PdfFormat.Money(item.UnitPrice, d.Currency));
                        Body(table.Cell()).AlignRight().Text(PdfFormat.Money(item.TotalPrice, d.Currency)).SemiBold();
                    }
                });

                c.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().PaddingRight(20).AlignBottom().Column(w =>
                    {
                        if (d.AmountInWords is not null)
                        {
                            w.Item().Text("YALNIZ").FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.12f);
                            w.Item().Text(d.AmountInWords.Replace("YALNIZ: ", string.Empty, StringComparison.Ordinal)).FontSize(8.5f).Italic();
                        }
                    });
                    row.ConstantItem(240).Column(t =>
                    {
                        TotalRow(t, "Ara Toplam", PdfFormat.Money(d.SubTotal, d.Currency));
                        if (d.DiscountAmount > 0)
                        {
                            TotalRow(t, "İndirim", "-" + PdfFormat.Money(d.DiscountAmount, d.Currency));
                            TotalRow(t, "KDV Matrahı", PdfFormat.Money(d.SubTotal - d.DiscountAmount, d.Currency));
                        }
                        TotalRow(t, $"KDV (%{d.VatRate.ToString("0.##", PdfFormat.Turkish)})", PdfFormat.Money(d.VatAmount, d.Currency));
                        t.Item().PaddingTop(5).Background(Black).PaddingHorizontal(10).PaddingVertical(7).Row(r =>
                        {
                            r.RelativeItem().AlignMiddle().Text("GENEL TOPLAM").FontSize(9).Bold().FontColor(Colors.White).LetterSpacing(0.08f);
                            r.AutoItem().Text(PdfFormat.Money(d.TotalAmount, d.Currency)).FontSize(13).Bold().FontColor(Colors.White);
                        });
                    });
                });
            });

            // Koşullar
            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(5).Text("TEKLİF KOŞULLARI").FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.12f);
                c.Item().Border(0.5f).BorderColor(Rule).Column(terms =>
                {
                    Term(terms, "Geçerlilik", $"Bu teklif {PdfFormat.Date(d.ValidUntil)} tarihine kadar geçerlidir.", first: true);
                    Term(terms, "Ödeme koşulları", d.PaymentTerms);
                    Term(terms, "Teslimat koşulları", d.DeliveryTerms);
                    Term(terms, "Açıklamalar", d.CustomerNotes);
                    Term(terms, "KDV", d.VatRate > 0
                        ? $"Fiyatlara %{d.VatRate.ToString("0.##", PdfFormat.Turkish)} KDV ayrıca eklenmiş, genel toplamda gösterilmiştir."
                        : "Bu teklifte KDV uygulanmamıştır.");
                    if (d.CompanyIban is not null)
                        Term(terms, "Banka", d.CompanyBankName is null ? $"IBAN: {d.CompanyIban}" : $"{d.CompanyBankName}  ·  IBAN: {d.CompanyIban}");
                });
            });

            // İmza alanı: sayfa sonuna bölünmez.
            col.Item().ShowEntire().Row(row =>
            {
                row.RelativeItem().Element(c => SignatureBox(c, d.CompanyName.ToUpper(PdfFormat.Turkish), "Teklifi hazırlayan", d.PreparedBy));
                row.ConstantItem(16);
                row.RelativeItem().Element(c => SignatureBox(c, "MÜŞTERİ ONAYI", "Yukarıdaki koşullarla teklifi kabul ediyoruz.", null, customer: true));
            });
        });
    }

    private static void InfoPanel(IContainer container, string title, Action<ColumnDescriptor> body) =>
        container.Background(Panel).PaddingHorizontal(12).PaddingVertical(9).Column(c =>
        {
            c.Spacing(2);
            c.Item().PaddingBottom(3).Text(title).FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.12f);
            body(c);
        });

    private static void Field(ColumnDescriptor col, string label, string? value, bool always = false)
    {
        if (string.IsNullOrWhiteSpace(value) && !always) return;
        col.Item().Row(r =>
        {
            r.ConstantItem(78).Text(label).FontSize(8).FontColor(Muted);
            r.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(9);
        });
    }

    private static void RouteEnd(ColumnDescriptor col, string label, string point, string? address)
    {
        col.Item().Text(label).FontSize(7.5f).SemiBold().FontColor(Muted).LetterSpacing(0.12f);
        col.Item().PaddingTop(3).Text(point).FontSize(11).Bold();
        if (!string.IsNullOrWhiteSpace(address)) col.Item().PaddingTop(2).Text(address).FontSize(8.5f).FontColor(Muted);
    }

    private static void TotalRow(ColumnDescriptor col, string label, string value) =>
        col.Item().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(2).Row(r =>
        {
            r.RelativeItem().Text(label).FontColor(Muted);
            r.AutoItem().Text(value).SemiBold();
        });

    private static void Term(ColumnDescriptor col, string label, string? value, bool first = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        col.Item().Element(c => first ? c : c.BorderTop(0.5f).BorderColor(Rule)).PaddingHorizontal(10).PaddingVertical(4).Row(r =>
        {
            r.ConstantItem(110).Text(label).FontSize(8).SemiBold();
            r.RelativeItem().Text(value).FontSize(8.5f);
        });
    }

    private static void SignatureBox(IContainer container, string title, string subtitle, string? name, bool customer = false) =>
        container.Border(1).BorderColor(Black).PaddingHorizontal(12).PaddingVertical(9).Column(c =>
        {
            c.Item().Text(title).FontSize(8.5f).Bold().LetterSpacing(0.08f);
            c.Item().PaddingTop(2).Text(subtitle).FontSize(7.5f).FontColor(Muted);
            if (customer)
            {
                c.Item().PaddingTop(9).Text("Ad Soyad / Unvan:").FontSize(8).FontColor(Muted);
                c.Item().PaddingTop(7).Text("Tarih:").FontSize(8).FontColor(Muted);
            }
            else
            {
                c.Item().PaddingTop(9).Text(name is null ? "Ad Soyad:" : $"Ad Soyad: {name}").FontSize(8).FontColor(Muted);
                c.Item().PaddingTop(7).Text("Tarih:").FontSize(8).FontColor(Muted);
            }
            c.Item().PaddingTop(22).LineHorizontal(0.5f).LineColor(Faint);
            c.Item().PaddingTop(3).AlignCenter().Text("Kaşe / İmza").FontSize(7.5f).FontColor(Faint);
        });

    private static void ComposeFooter(IContainer container, TransportOfferDocument d, DateTime generated)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Rule);
            col.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text(Join("  ·  ", d.CompanyName, d.CompanyPhone, d.CompanyEmail, d.CompanyWebsite) ?? d.CompanyName)
                    .FontSize(7.5f).FontColor(Faint);
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Faint));
                    t.Span($"{d.OfferNumber}  ·  {PdfFormat.DateTime(generated)}  ·  Sayfa ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }

    private static IContainer Head(IContainer c) =>
        c.Background(Black).PaddingVertical(6).PaddingHorizontal(6).DefaultTextStyle(t => t.FontSize(7.5f).SemiBold().FontColor(Colors.White).LetterSpacing(0.08f));

    private static IContainer Body(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(5).PaddingHorizontal(6);

    private static string Quantity(decimal quantity, string? unit)
    {
        var q = quantity.ToString("#,##0.###", PdfFormat.Turkish);
        return string.IsNullOrWhiteSpace(unit) ? q : $"{q} {unit}";
    }

    private static string? Join(string separator, params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return list.Count == 0 ? null : string.Join(separator, list);
    }
}
