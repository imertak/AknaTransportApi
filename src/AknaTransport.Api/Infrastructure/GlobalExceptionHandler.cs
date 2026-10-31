using AknaTransport.Business.Common;
using AknaTransport.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AknaTransport.Api.Infrastructure;

/// <summary>
/// Tüm hataları standart <see cref="BaseResponse"/> biçimine ve doğru HTTP durum koduna dönüştürür.
/// Beklenmeyen hatalarda istemciye iç ayrıntı gönderilmez.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, response) = Map(exception);
        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Beklenmeyen hata. {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("İstek reddedildi ({Status}): {Message}", status, exception.Message);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(response, cancellationToken);
        return true;
    }

    private (int Status, BaseResponse Response) Map(Exception exception) => exception switch
    {
        ValidationException v => (StatusCodes.Status400BadRequest, BaseResponse.Fail("Doğrulama hatası.",
            v.Errors.Select(e => new ErrorDetail(ToCamel(e.PropertyName), e.ErrorMessage)).ToList())),
        BusinessValidationException b => (StatusCodes.Status400BadRequest, BaseResponse.Fail(b.Message, b.Errors)),
        UnauthorizedException u => (StatusCodes.Status401Unauthorized, BaseResponse.Fail(u.Message)),
        ForbiddenException f => (StatusCodes.Status403Forbidden, BaseResponse.Fail(f.Message)),
        NotFoundException n => (StatusCodes.Status404NotFound, BaseResponse.Fail(n.Message)),
        ConflictException c => (StatusCodes.Status409Conflict, BaseResponse.Fail(c.Message)),
        DomainException d => (StatusCodes.Status409Conflict, BaseResponse.Fail(d.Message)),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
            BaseResponse.Fail("Kayıt başka bir işlem tarafından değiştirildi. Lütfen yenileyip tekrar deneyin.")),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => (StatusCodes.Status409Conflict,
            BaseResponse.Fail("Bu kayıt zaten mevcut (benzersizlik kuralı).")),
        IntegrationUnavailableException i => (StatusCodes.Status503ServiceUnavailable, BaseResponse.Fail(i.Message)),
        BadHttpRequestException bad => (bad.StatusCode, BaseResponse.Fail("Geçersiz istek.")),
        _ => (StatusCodes.Status500InternalServerError, BaseResponse.Fail(environment.IsDevelopment()
            ? $"Beklenmeyen bir hata oluştu: {exception.Message}"
            : "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.")),
    };

    private static string ToCamel(string name) =>
        string.Join('.', name.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
