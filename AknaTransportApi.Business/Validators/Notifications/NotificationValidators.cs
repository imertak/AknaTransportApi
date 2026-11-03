using FluentValidation;

namespace AknaTransportApi.Business.Validators.Notifications;

public sealed class RegisterPushDeviceRequestValidator : AbstractValidator<RegisterPushDeviceRequest>
{
    public RegisterPushDeviceRequestValidator()
    {
        RuleFor(x => x.Token).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Bildirim anahtarı boş olamaz.")
            .MaximumLength(PushDevice.TokenMaxLength)
            .Must(NotificationTokenRules.IsExpoToken).WithMessage("Geçersiz bildirim anahtarı.");
        RuleFor(x => x.Platform).Must(p => p is null || p.Trim().ToLowerInvariant() is "ios" or "android" or "web")
            .WithMessage("Platform ios veya android olmalıdır.");
    }
}

public sealed class UnregisterPushDeviceRequestValidator : AbstractValidator<UnregisterPushDeviceRequest>
{
    public UnregisterPushDeviceRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(PushDevice.TokenMaxLength);
    }
}

public static class NotificationTokenRules
{
    /// <summary>Expo push token biçimi: ExponentPushToken[...] veya ExpoPushToken[...].</summary>
    public static bool IsExpoToken(string token)
    {
        var t = token.Trim();
        return (t.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || t.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
               && t.EndsWith(']');
    }
}
