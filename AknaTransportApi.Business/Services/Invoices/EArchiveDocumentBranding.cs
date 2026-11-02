namespace AknaTransportApi.Business.Services.Invoices;

/// <summary>
/// GİB portalındaki fatura görüntüsünü PDF'e çevirmeden önce AKNA logosunu ekler. Logo, faturadaki karekodun hemen
/// altına, karekodla aynı genişlikte ortalanarak yerleştirilir (karekodun altındaki boşluk). Karekod bulunamazsa
/// sayfanın en üstüne ayrı bir satır olarak eklenir. Faturanın içeriğine ve alanlarına dokunulmaz.
/// </summary>
internal static class EArchiveDocumentBranding
{
    internal const string LogoId = "akna-logo";

    private const string Logo =
        $"""<div id="{LogoId}" style="text-align:right;margin:0 0 4mm"><span style="display:inline-block;background:#000;border-radius:4px;padding:9px 12px;line-height:0">""" +
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 436 102" width="96" height="22.5" fill="#fff">""" +
        """<polygon points="60,0 120.5,102 0,102"/><polygon points="121,6 213.5,6 179,55 213.5,102 121,102"/>""" +
        """<polygon points="219,4.5 267,40 267,6 314.5,6 314.5,102 219,102"/><polygon points="376,0 436,102 316,102"/></svg></span></div>""";

    // Karekod: GİB görüntüsünde #qrcode kapsayıcısı; yoksa sayfanın üst kısmındaki en sağdaki kare görsel.
    // Tarayıcı sayfa yüklendikten sonra yazdırdığı için logo, karekod çizildikten sonra yerleştirilir.
    private const string PlaceUnderQr = $$"""
        <script>
        (function () {
          function place() {
            var logo = document.getElementById('{{LogoId}}');
            if (!logo) return;
            var qr = document.getElementById('qrcode') || document.querySelector('[id*="qr" i]:not(#{{LogoId}})');
            if (!qr) {
              var squares = Array.prototype.filter.call(document.querySelectorAll('img, canvas'), function (e) {
                var r = e.getBoundingClientRect();
                return r.width >= 60 && Math.abs(r.width - r.height) <= 4 && r.top < 500;
              });
              squares.sort(function (a, b) { return b.getBoundingClientRect().right - a.getBoundingClientRect().right; });
              qr = squares[0];
            }
            if (!qr || !qr.parentNode) return;
            var width = Math.round(qr.getBoundingClientRect().width) || 140;
            logo.style.textAlign = 'center';
            logo.style.margin = '4mm 0 0';
            logo.style.width = width + 'px';
            if (qr.parentNode.getAttribute && qr.parentNode.getAttribute('align') === 'right') logo.style.marginLeft = 'auto';
            qr.parentNode.insertBefore(logo, qr.nextSibling);
          }
          if (document.readyState === 'complete') place(); else window.addEventListener('load', place);
        })();
        </script>
        """;

    public static string AddLogo(string html)
    {
        var withLogo = InsertAfterBodyOpen(html, Logo);
        var end = withLogo.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end >= 0 ? withLogo.Insert(end, PlaceUnderQr) : withLogo + PlaceUnderQr;
    }

    private static string InsertAfterBodyOpen(string html, string fragment)
    {
        var body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (body < 0) return fragment + html;
        var close = html.IndexOf('>', body);
        return close < 0 ? fragment + html : html.Insert(close + 1, fragment);
    }
}
