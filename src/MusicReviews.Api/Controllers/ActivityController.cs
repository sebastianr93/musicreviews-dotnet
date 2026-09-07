using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Activity;
using MusicReviews.Application.Activity.Dtos;
using MusicReviews.Application.Common.Models;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Timelines de actividad. Se paginan por cursor y no por numero de pagina: en un feed
/// que crece por arriba, el offset repite filas entre pagina y pagina.
/// </summary>
[ApiController]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activity;

    public ActivityController(IActivityService activity)
    {
        _activity = activity;
    }

    /// <summary>Actividad de un usuario: lo que publico, comento y voto.</summary>
    [HttpGet("api/users/{userName}/activity")]
    [AllowAnonymous]
    [ProducesResponseType<CursorPage<ActivityEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserActivity(
        string userName,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = CursorRequest.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var result = await _activity.GetUserActivityAsync(
            userName, new CursorRequest(cursor, limit), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Actividad combinada de los usuarios a los que seguis.
    /// Devuelve una pagina vacia si todavia no seguis a nadie.
    /// </summary>
    [HttpGet("api/feed")]
    [Authorize]
    [ProducesResponseType<CursorPage<ActivityEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFollowingFeed(
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = CursorRequest.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var result = await _activity.GetFollowingFeedAsync(
            new CursorRequest(cursor, limit), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
