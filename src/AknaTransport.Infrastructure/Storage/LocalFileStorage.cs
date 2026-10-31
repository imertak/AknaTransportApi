using AknaTransport.Business.Abstractions;
using Microsoft.Extensions.Options;

namespace AknaTransport.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Dosyaların saklanacağı kök klasör. Web kökünün dışında olmalıdır.</summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>
/// Yerel disk depolaması. Dosya adları sistem tarafından üretilir; okuma sırasında yolun kök klasör
/// dışına çıkması (path traversal) engellenir.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(string category, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var relative = Path.Combine(Sanitize(category), Sanitize(fileName));
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp";
        await File.WriteAllBytesAsync(temp, content, cancellationToken);
        File.Move(temp, full, overwrite: true);
        return relative.Replace('\\', '/');
    }

    public async Task<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = Resolve(relativePath);
        return File.Exists(full) ? await File.ReadAllBytesAsync(full, cancellationToken) : null;
    }

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Geçersiz dosya yolu.");
        return full;
    }

    private static string Sanitize(string value)
    {
        var name = Path.GetFileName(value);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") throw new ArgumentException("Geçersiz dosya adı.");
        return name;
    }
}
