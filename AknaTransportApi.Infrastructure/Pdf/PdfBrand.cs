using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AknaTransportApi.Infrastructure.Pdf;

/// <summary>
/// AKNA logosu: siyah zemin üzerinde geometrik beyaz "AKNA" harfleri (vektör, SVG). Harici görsel dosyası gerektirmez.
/// </summary>
internal static class PdfBrand
{
    /// <summary>Logonun harfleri; viewBox 436 x 102.</summary>
    internal const string LetterPolygons = """
        <polygon points="60,0 120.5,102 0,102"/>
        <polygon points="121,6 213.5,6 179,55 213.5,102 121,102"/>
        <polygon points="219,4.5 267,40 267,6 314.5,6 314.5,102 219,102"/>
        <polygon points="376,0 436,102 316,102"/>
        """;

    private static readonly string Svg =
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 436 102" fill="#FFFFFF">{LetterPolygons}</svg>""";

    /// <param name="width">Harflerin genişliği (pt). Siyah zemin bunun etrafına eklenir.</param>
    public static void Logo(IContainer container, float width = 74f)
    {
        var pad = width * 0.14f;
        container.Background("#000000").CornerRadius(width * 0.05f).PaddingHorizontal(pad).PaddingVertical(pad * 0.85f)
            .Width(width).Height(width * 102f / 436f).Svg(Svg);
    }
}
