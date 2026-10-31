namespace AknaTransportApi.Business.Common;

/// <summary>Uygulama katmanı hatalarının tabanı. API katmanı tipine göre HTTP durum kodu üretir.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message)
{
    public static NotFoundException For(string entity) => new($"{entity} bulunamadı.");
}

/// <summary>403: kullanıcı kimliği doğrulanmış ama bu kaynağa yetkisi yok.</summary>
public sealed class ForbiddenException(string message = "Bu işlem için yetkiniz yok.") : AppException(message);

/// <summary>401: kimlik doğrulama başarısız.</summary>
public sealed class UnauthorizedException(string message) : AppException(message);

/// <summary>409: kaynak mevcut durumla çelişiyor (mükerrer kayıt, uygun olmayan durum vb.).</summary>
public sealed class ConflictException(string message) : AppException(message);

/// <summary>400: iş kuralı doğrulama hatası.</summary>
public sealed class BusinessValidationException(string message, IReadOnlyList<ErrorDetail>? errors = null) : AppException(message)
{
    public IReadOnlyList<ErrorDetail> Errors { get; } = errors ?? [];
}

/// <summary>503: harici bir entegrasyon yapılandırılmamış veya erişilemiyor.</summary>
public sealed class IntegrationUnavailableException(string message) : AppException(message);
