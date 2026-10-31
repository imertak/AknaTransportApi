using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransport.Api.Infrastructure;

[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(BaseResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(BaseResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(BaseResponse), StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<BaseResponse<T>> Success<T>(T data, string? message = null) => Ok(BaseResponse<T>.Success(data, message));

    protected ActionResult<BaseResponse<T>> Created<T>(string actionName, object routeValues, T data, string? message = null) =>
        CreatedAtAction(actionName, routeValues, BaseResponse<T>.Success(data, message));

    protected ActionResult<BaseResponse> Done(string message) => Ok(BaseResponse.Ok(message));

    /// <summary>PDF dosyası döndürür; tarayıcıda içerik türü tahmini ve önbellekleme kapatılır.</summary>
    protected FileContentResult Pdf(FileDownload file)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Location = "location";
    public const string PublicTracking = "public-tracking";
}
