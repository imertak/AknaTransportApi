namespace AknaTransportApi.Business.Services.Invoices;

/// <summary>
/// GİB portalındaki fatura görüntüsünü PDF'e çevirmeden önce sayfanın en üstüne AKNA logosunu ekler.
/// Logo, belgenin önüne ayrı bir satır olarak eklenir; faturanın içeriğine ve alanlarına dokunulmaz.
/// </summary>
internal static class EArchiveDocumentBranding
{
    private const string Logo =
        """<div style="text-align:right;margin:0 0 4mm"><span style="display:inline-block;background:#000;border-radius:4px;padding:9px 12px;line-height:0">""" +
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 436 102" width="96" height="22.5" fill="#fff">""" +
        """<polygon points="60,0 120.5,102 0,102"/><polygon points="121,6 213.5,6 179,55 213.5,102 121,102"/>""" +
        """<polygon points="219,4.5 267,40 267,6 314.5,6 314.5,102 219,102"/><polygon points="376,0 436,102 316,102"/></svg></span></div>""";

    public static string AddLogo(string html)
    {
        var body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (body < 0) return Logo + html;
        var close = html.IndexOf('>', body);
        return close < 0 ? Logo + html : html.Insert(close + 1, Logo);
    }
}
