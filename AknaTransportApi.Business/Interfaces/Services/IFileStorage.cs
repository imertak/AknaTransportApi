namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Dosya depolama. Yollar her zaman sistem tarafından üretilir, kullanıcıdan alınmaz.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(string category, string fileName, byte[] content, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default);
    /// <summary>Dosyayı siler; yoksa bir şey yapmaz.</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
