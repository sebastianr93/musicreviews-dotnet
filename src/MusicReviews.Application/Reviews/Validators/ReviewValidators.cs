using FluentValidation;
using MusicReviews.Application.Reviews.Dtos;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Application.Reviews.Validators;

public sealed class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewRequestValidator()
    {
        RuleFor(x => x.AlbumMusicBrainzId)
            .NotEmpty().WithMessage("Falta el identificador del álbum.")
            .Must(value => Guid.TryParse(value, out _))
                .WithMessage("El identificador de MusicBrainz no es un MBID válido.");

        RuleFor(x => x.Score)
            .InclusiveBetween(Review.MinScore, Review.MaxScore)
            .WithMessage($"El puntaje tiene que estar entre {Review.MinScore} y {Review.MaxScore}.");

        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("La reseña no puede estar vacía.")
            .MaximumLength(Review.TextMaxLength)
            .WithMessage($"La reseña no puede superar los {Review.TextMaxLength} caracteres.");
    }
}

public sealed class UpdateReviewRequestValidator : AbstractValidator<UpdateReviewRequest>
{
    public UpdateReviewRequestValidator()
    {
        RuleFor(x => x.Score)
            .InclusiveBetween(Review.MinScore, Review.MaxScore)
            .WithMessage($"El puntaje tiene que estar entre {Review.MinScore} y {Review.MaxScore}.");

        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("La reseña no puede estar vacía.")
            .MaximumLength(Review.TextMaxLength)
            .WithMessage($"La reseña no puede superar los {Review.TextMaxLength} caracteres.");
    }
}
