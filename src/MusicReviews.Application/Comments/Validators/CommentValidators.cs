using FluentValidation;
using MusicReviews.Application.Comments.Dtos;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Application.Comments.Validators;

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("El comentario no puede estar vacio.")
            .MaximumLength(Comment.TextMaxLength)
            .WithMessage($"El comentario no puede superar los {Comment.TextMaxLength} caracteres.");

        RuleFor(x => x.ParentCommentId)
            .GreaterThan(0).When(x => x.ParentCommentId.HasValue)
            .WithMessage("El identificador del comentario padre no es valido.");
    }
}

public sealed class UpdateCommentRequestValidator : AbstractValidator<UpdateCommentRequest>
{
    public UpdateCommentRequestValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("El comentario no puede estar vacio.")
            .MaximumLength(Comment.TextMaxLength)
            .WithMessage($"El comentario no puede superar los {Comment.TextMaxLength} caracteres.");
    }
}
