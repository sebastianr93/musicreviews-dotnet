using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Reviews;
using MusicReviews.Application.Reviews.Dtos;

namespace MusicReviews.Api.Controllers;

[ApiController]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ReviewsController(IReviewService reviews)
    {
        _reviews = reviews;
    }

    /// <summary>Detalle de una review.</summary>
    [HttpGet("api/reviews/{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _reviews.GetByIdAsync(id, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Reviews de un album. Devuelve una pagina vacia si el album aun no tiene ninguna.</summary>
    [HttpGet("api/albums/{musicBrainzId:guid}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<ReviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByAlbum(
        Guid musicBrainzId,
        [FromQuery] ReviewSortOrder sort = ReviewSortOrder.Newest,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviews.GetByAlbumAsync(
            musicBrainzId.ToString(), new PageRequest(page, pageSize), sort, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Reviews escritas por un usuario.</summary>
    [HttpGet("api/users/{userName}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByUser(
        string userName,
        [FromQuery] ReviewSortOrder sort = ReviewSortOrder.Newest,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviews.GetByUserAsync(
            userName, new PageRequest(page, pageSize), sort, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Crea la review del usuario autenticado. Si el album no estaba en el catalogo
    /// local, se trae de MusicBrainz y se persiste antes de guardar.
    /// </summary>
    [HttpPost("api/reviews")]
    [Authorize]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _reviews.CreateAsync(request, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : this.Problem(result.Error);
    }

    /// <summary>Edita una review. Solo el autor.</summary>
    [HttpPut("api/reviews/{id:int}")]
    [Authorize]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        UpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _reviews.UpdateAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Borra una review. El autor, o un administrador moderando.</summary>
    [HttpDelete("api/reviews/{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _reviews.DeleteAsync(id, cancellationToken);

        return result.IsSuccess ? NoContent() : this.Problem(result.Error);
    }
}
