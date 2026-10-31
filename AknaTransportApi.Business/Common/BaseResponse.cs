namespace AknaTransport.Business.Common;

/// <summary>Tüm endpointlerin ortak yanıt zarfı.</summary>
public class BaseResponse<T>
{
    public bool IsSuccess { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<ErrorDetail> Errors { get; init; } = [];

    public static BaseResponse<T> Success(T data, string? message = null) => new() { IsSuccess = true, Data = data, Message = message };
}

public class BaseResponse : BaseResponse<object?>
{
    public static BaseResponse Ok(string? message = null) => new() { IsSuccess = true, Message = message };

    public static BaseResponse Fail(string message, IReadOnlyList<ErrorDetail>? errors = null)
        => new() { IsSuccess = false, Message = message, Errors = errors ?? [] };
}

/// <summary>Alan bazlı hata. Field boşsa genel hatadır.</summary>
public sealed record ErrorDetail(string? Field, string Message);
