using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Likes.Dtos;
using MusicReviews.Domain.Enums;

namespace MusicReviews.Application.Likes;

public interface ILikeService
{
    /// <summary>
    /// Aplica el voto del usuario autenticado sobre una review o un comentario.
    /// Semantica de toggle: el mismo voto lo retira, el contrario lo cambia.
    /// </summary>
    Task<Result<VoteResultDto>> ToggleAsync(
        LikeTargetType targetType,
        int targetId,
        ToggleVoteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Retira el voto del usuario, si lo habia. Idempotente.</summary>
    Task<Result<VoteResultDto>> RemoveAsync(
        LikeTargetType targetType,
        int targetId,
        CancellationToken cancellationToken = default);
}
