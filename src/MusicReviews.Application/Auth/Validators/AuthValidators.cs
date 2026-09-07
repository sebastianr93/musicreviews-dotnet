using System.Text.RegularExpressions;
using FluentValidation;
using MusicReviews.Application.Auth.Dtos;

namespace MusicReviews.Application.Auth.Validators;

public sealed partial class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    // Mismo criterio que Identity: letras, digitos y - . _ @ +
    [GeneratedRegex(@"^[a-zA-Z0-9\-._@+]+$")]
    private static partial Regex UserNameRegex();

    public RegisterRequestValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("El nombre de usuario es obligatorio.")
            .MinimumLength(3).WithMessage("El nombre de usuario tiene que tener al menos 3 caracteres.")
            .MaximumLength(32).WithMessage("El nombre de usuario no puede superar los 32 caracteres.")
            .Must(value => UserNameRegex().IsMatch(value))
                .When(x => !string.IsNullOrWhiteSpace(x.UserName))
                .WithMessage("El nombre de usuario solo admite letras, dígitos y los símbolos - . _ @ +");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El email es obligatorio.")
            .EmailAddress().WithMessage("El email no tiene un formato válido.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MinimumLength(8).WithMessage("La contraseña tiene que tener al menos 8 caracteres.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("La contraseña tiene que incluir al menos una mayúscula.")
            .Matches("[a-z]").WithMessage("La contraseña tiene que incluir al menos una minúscula.")
            .Matches("[0-9]").WithMessage("La contraseña tiene que incluir al menos un dígito.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserNameOrEmail)
            .NotEmpty().WithMessage("Indicá tu usuario o email.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MaximumLength(128);
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("El refresh token es obligatorio.")
            .MaximumLength(512);
    }
}

public sealed class RevokeTokenRequestValidator : AbstractValidator<RevokeTokenRequest>
{
    public RevokeTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("El refresh token es obligatorio.")
            .MaximumLength(512);
    }
}
