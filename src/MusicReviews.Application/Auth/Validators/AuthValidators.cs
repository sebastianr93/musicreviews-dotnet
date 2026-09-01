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
                .WithMessage("El nombre de usuario solo admite letras, digitos y los simbolos - . _ @ +");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El email es obligatorio.")
            .EmailAddress().WithMessage("El email no tiene un formato valido.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contrasenia es obligatoria.")
            .MinimumLength(8).WithMessage("La contrasenia tiene que tener al menos 8 caracteres.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("La contrasenia tiene que incluir al menos una mayuscula.")
            .Matches("[a-z]").WithMessage("La contrasenia tiene que incluir al menos una minuscula.")
            .Matches("[0-9]").WithMessage("La contrasenia tiene que incluir al menos un digito.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserNameOrEmail)
            .NotEmpty().WithMessage("Indica tu usuario o email.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contrasenia es obligatoria.")
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
