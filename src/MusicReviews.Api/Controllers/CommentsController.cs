using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Comments;
using MusicReviews.Application.Comments.Dtos;

namespace MusicReviews.Api.Controllers;

[ApiController]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _comments;

    public CommentsController(ICommentService comments)
    {
        _comments = comments;
    }

    /// <summary>
    /// Hilo completo de comentarios de una review, ya anidado.
    /// Se resuelve con una sola consulta a la base, sin importar la profundidad.
    /// </summary>
    [HttpGet("api/reviews/{reviewId:int}/comments")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CommentNodeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTree(
        int reviewId,
        [FromQuery] CommentSortOrder sort = CommentSortOrder.Oldest,
        CancellationToken cancellationToken = default)
    {
        var result = await _comments.GetTreeAsync(reviewId, sort, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Detalle de un comentario, sin sus respuestas.</summary>
    [HttpGet("api/comments/{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType<CommentNodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _comments.GetByIdAsync(id, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Publica un comentario. Con <c>parentCommentId</c> es una respuesta;
    /// sin el, un comentario de primer nivel.
    /// </summary>
    [HttpPost("api/reviews/{reviewId:int}/comments")]
    [Authorize]
    [ProducesResponseType<CommentNodeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        int reviewId,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _comments.CreateAsync(reviewId, request, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : this.Problem(result.Error);
    }

    /// <summary>Edita un comentario. Solo el autor.</summary>
    [HttpPut("api/comments/{id:int}")]
    [Authorize]
    [ProducesResponseType<CommentNodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        UpdateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _comments.UpdateAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Borra un comentario. El autor, o un administrador moderando.
    /// Es un borrado logico: el nodo sigue en el hilo para no dejar huerfanas
    /// las respuestas de otros usuarios.
    /// </summary>
    [HttpDelete("api/comments/{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _comments.DeleteAsync(id, cancellationToken);

        return result.IsSuccess ? NoContent() : this.Problem(result.Error);
    }
}
