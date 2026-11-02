using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.Pdf;

public sealed class HtmlPdfOptions
{
    public const string SectionName = "Pdf";

    /// <summary>
    /// Chrome, Chromium, chrome-headless-shell veya Edge'in tam yolu. Boşsa bilinen kurulum yerleri denenir
    /// (Windows'ta Edge her zaman bulunur). Tarayıcı yoksa GİB belgesi PDF'e çevrilmez, e-postaya HTML olarak eklenir.
    /// </summary>
    public string? ChromePath { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// HTML'i, sunucudaki Chromium tabanlı tarayıcının "--print-to-pdf" komutuyla PDF'e çevirir. NuGet paketi gerektirmez.
/// Her çağrı ayrı geçici profil ve dosyalarla çalışır; süre aşılırsa tarayıcı süreci sonlandırılır.
/// </summary>
public sealed class ChromeHtmlPdfRenderer(IOptions<HtmlPdfOptions> options, ILogger<ChromeHtmlPdfRenderer> logger) : IHtmlPdfRenderer
{
    private readonly HtmlPdfOptions _options = options.Value;
    private readonly Lazy<string?> _path = new(() => Resolve(options.Value.ChromePath));

    public bool IsAvailable => _path.Value is not null;

    public async Task<byte[]> RenderAsync(string html, CancellationToken ct)
    {
        var exe = _path.Value ?? throw new IntegrationUnavailableException("PDF'e çevirmek için sunucuda Chrome/Edge bulunamadı (Pdf__ChromePath).");
        var dir = Directory.CreateTempSubdirectory("akna-pdf-");
        try
        {
            var input = Path.Combine(dir.FullName, "belge.html");
            var output = Path.Combine(dir.FullName, "belge.pdf");
            await File.WriteAllTextAsync(input, WithA4(html), ct);

            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
                     {
                         "--headless", "--disable-gpu", "--no-sandbox", "--no-first-run", "--no-default-browser-check",
                         "--disable-extensions", "--disable-background-networking", "--no-pdf-header-footer",
                         $"--user-data-dir={Path.Combine(dir.FullName, "profile")}", $"--print-to-pdf={output}", new Uri(input).AbsoluteUri,
                     })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new IntegrationUnavailableException("Tarayıcı başlatılamadı.");
            var stderr = process.StandardError.ReadToEndAsync(ct);
            _ = process.StandardOutput.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) throw;
                throw new IntegrationUnavailableException($"PDF'e çevirme {_options.TimeoutSeconds} saniyede tamamlanmadı.");
            }

            if (!File.Exists(output) || new FileInfo(output).Length == 0)
            {
                var error = await stderr;
                logger.LogWarning("Tarayıcı PDF üretmedi. ExitCode: {ExitCode}, Hata: {Error}", process.ExitCode, error.Length > 500 ? error[^500..] : error);
                throw new IntegrationUnavailableException("Tarayıcı PDF üretmedi.");
            }
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Tarayıcının varsayılan kâğıdı Letter'dır; Türkiye'de kullanılan A4 sayfa düzeni eklenir.</summary>
    private static string WithA4(string html)
    {
        const string style = "<style>@page { size: A4; margin: 10mm; }</style>";
        var head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        return head >= 0 ? html.Insert(head + "<head>".Length, style) : style + html;
    }

    private static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            ]
            : OperatingSystem.IsMacOS()
                ? ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"]
                : ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome-stable", "/usr/bin/google-chrome", "/usr/bin/chrome-headless-shell"];
        return candidates.FirstOrDefault(File.Exists);
    }
}
