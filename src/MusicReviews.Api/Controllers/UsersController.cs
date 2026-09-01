using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Users;
using MusicReviews.Application.Users.Dtos;

namespace MusicReviews.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
    }

    // Las rutas literales ("me") tienen precedencia sobre las de plantilla
    // ("{userName}"), asi que no hay ambiguedad con un usuario que se llame "me".

    /// <summary>Perfil del usuario autenticado.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var result = await _users.GetMyProfileAsync(cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Edita el perfil propio. Reemplaza los campos: un null los borra.</summary>
    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMyProfile(
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _users.UpdateMyProfileAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Agrega un artista a favoritos. Idempotente.</summary>
    [HttpPost("me/favorites")]
    [Authorize]
    [ProducesResponseType<FavoriteArtistDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavorite(
        AddFavoriteArtistRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _users.AddFavoriteAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Quita un artista de favoritos. Idempotente.</summary>
    [HttpDelete("me/favorites/{artistMusicBrainzId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveFavorite(
        Guid artistMusicBrainzId,
        CancellationToken cancellationToken)
    {
        var result = await _users.RemoveFavoriteAsync(artistMusicBrainzId.ToString(), cancellationToken);

        return result.IsSuccess ? NoContent() : this.Problem(result.Error);
    }

    /// <summary>Perfil publico de un usuario.</summary>
    [HttpGet("{userName}")]
    [AllowAnonymous]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(string userName, CancellationToken cancellationToken)
    {
        var result = await _users.GetProfileAsync(userName, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Artistas favoritos de un usuario.</summary>
    [HttpGet("{userName}/favorites")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<FavoriteArtistDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFavorites(string userName, CancellationToken cancellationToken)
    {
        var result = await _users.GetFavoritesAsync(userName, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
