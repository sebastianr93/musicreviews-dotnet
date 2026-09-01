using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Likes;
using MusicReviews.Application.Likes.Dtos;
using MusicReviews.Domain.Enums;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Votos sobre reviews y comentarios. Las rutas quedan anidadas bajo cada recurso
/// (<c>/api/reviews/{id}/likes</c>) en vez de exponer el par (targetType, targetId):
/// el cliente no tiene por que conocer que por debajo hay una sola tabla polimorfica.
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class LikesController : ControllerBase
{
    private readonly ILikeService _likes;

    public LikesController(ILikeService likes)
    {
        _likes = likes;
    }

    /// <summary>
    /// Vota una review. Es un toggle: el mismo voto lo retira, el contrario lo cambia.
    /// </summary>
    [HttpPost("api/reviews/{reviewId:int}/likes")]
    [ProducesResponseType<VoteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ToggleReviewVote(
        int reviewId,
        ToggleVoteRequest request,
        CancellationToken cancellationToken) =>
        ToggleAsync(LikeTargetType.Review, reviewId, request, cancellationToken);

    /// <summary>Retira el voto sobre una review. Idempotente.</summary>
    [HttpDelete("api/reviews/{reviewId:int}/likes")]
    [ProducesResponseType<VoteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RemoveReviewVote(int reviewId, CancellationToken cancellationToken) =>
        RemoveAsync(LikeTargetType.Review, reviewId, cancellationToken);

    /// <summary>Vota un comentario. Mismo toggle que en reviews.</summary>
    [HttpPost("api/comments/{commentId:int}/likes")]
    [ProducesResponseType<VoteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ToggleCommentVote(
        int commentId,
        ToggleVoteRequest request,
        CancellationToken cancellationToken) =>
        ToggleAsync(LikeTargetType.Comment, commentId, request, cancellationToken);

    /// <summary>Retira el voto sobre un comentario. Idempotente.</summary>
    [HttpDelete("api/comments/{commentId:int}/likes")]
    [ProducesResponseType<VoteResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RemoveCommentVote(int commentId, CancellationToken cancellationToken) =>
        RemoveAsync(LikeTargetType.Comment, commentId, cancellationToken);

    private async Task<IActionResult> ToggleAsync(
        LikeTargetType targetType,
        int targetId,
        ToggleVoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _likes.ToggleAsync(targetType, targetId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    private async Task<IActionResult> RemoveAsync(
        LikeTargetType targetType,
        int targetId,
        CancellationToken cancellationToken)
    {
        var result = await _likes.RemoveAsync(targetType, targetId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
