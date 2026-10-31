using FluentValidation;

namespace AknaTransport.Business.Features.Users;

public static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("Şifre zorunludur.")
        .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
        .MaximumLength(128).WithMessage("Şifre en fazla 128 karakter olabilir.")
        .Matches("[A-Za-zÇĞİÖŞÜçğıöşü]").WithMessage("Şifre en az bir harf içermelidir.")
        .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.");

    public static IRuleBuilderOptions<T, string> ValidPhone<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("Telefon numarası zorunludur.")
        .Matches(@"^\+?[0-9 ()\-]{10,20}$").WithMessage("Geçerli bir telefon numarası girin.");
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Ad soyad zorunludur.").MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().WithMessage("E-posta zorunludur.").EmailAddress().WithMessage("Geçerli bir e-posta girin.").MaximumLength(200);
        RuleFor(x => x.PhoneNumber).ValidPhone();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Ad soyad zorunludur.").MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Geçerli bir e-posta girin.").MaximumLength(200);
        RuleFor(x => x.PhoneNumber).ValidPhone();
        RuleFor(x => x.Role).IsInEnum();
    }
}
