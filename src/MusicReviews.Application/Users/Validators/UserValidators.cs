using FluentValidation;
using MusicReviews.Application.Users.Dtos;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Application.Users.Validators;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Bio)
            .MaximumLength(ApplicationUser.BioMaxLength)
            .WithMessage($"La bio no puede superar los {ApplicationUser.BioMaxLength} caracteres.");

        RuleFor(x => x.AvatarUrl)
            .MaximumLength(ApplicationUser.AvatarUrlMaxLength)
            .Must(BeAnAbsoluteHttpUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.AvatarUrl))
                .WithMessage("El avatar tiene que ser una URL http o https absoluta.");
    }

    /// <summary>
    /// Solo http/https y absoluta: una URL relativa o un esquema como javascript:
    /// terminaria renderizada en el perfil publico de todos los que lo visiten.
    /// </summary>
    private static bool BeAnAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class AddFavoriteArtistRequestValidator : AbstractValidator<AddFavoriteArtistRequest>
{
    public AddFavoriteArtistRequestValidator()
    {
        RuleFor(x => x.ArtistMusicBrainzId)
            .NotEmpty().WithMessage("Falta el identificador del artista.")
            .Must(value => Guid.TryParse(value, out _))
                .WithMessage("El identificador de MusicBrainz no es un MBID valido.");
    }
}
