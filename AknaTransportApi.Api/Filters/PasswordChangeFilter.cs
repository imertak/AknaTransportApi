using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AknaTransportApi.Api.Filters;

/// <summary>Geçici şifre ile giriş yapmış kullanıcının da erişebileceği endpointleri işaretler.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhenPasswordChangeRequiredAttribute : Attribute;

/// <summary>
/// Yönetici tarafından oluşturulan (geçici şifreli) hesaplar, şifrelerini değiştirene kadar
/// yalnızca işaretli endpointleri (me, change-password, logout) kullanabilir.
/// </summary>
public sealed class PasswordChangeFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true || user.FindFirst(AppClaimTypes.MustChangePassword)?.Value != "true")
            return Task.CompletedTask;

        var allowed = context.ActionDescriptor.EndpointMetadata.OfType<AllowWhenPasswordChangeRequiredAttribute>().Any();
        if (!allowed)
        {
            context.Result = new ObjectResult(BaseResponse.Fail("Devam etmeden önce geçici şifrenizi değiştirmeniz gerekiyor."))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
        return Task.CompletedTask;
    }
}
