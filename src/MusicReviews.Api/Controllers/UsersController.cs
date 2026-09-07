using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Users;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Users.Dtos;

namespace MusicReviews.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Tope del cuerpo de la subida. Tiene que ser una constante porque
    /// <c>RequestSizeLimit</c> es un atributo y los atributos no leen configuracion; el
    /// valor efectivo lo define <c>Avatars:MaxSizeBytes</c> y se comprueba en el
    /// servicio. Este numero es solo el corte duro para que un cuerpo enorme ni siquiera
    /// se lea.
    /// </summary>
    private const long MaxAvatarBytes = 8 * 1024 * 1024;

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

    /// <summary>
    /// Sube el avatar. Multipart, campo <c>file</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El tope de tamanio se declara en el atributo <b>y</b> se vuelve a comprobar en el
    /// servicio. No es redundancia: el atributo corta la subida antes de leer el cuerpo
    /// —que es lo que evita que alguien mande un archivo de un giga—, y la comprobacion
    /// del servicio es la que produce un mensaje que el usuario entiende.
    /// </para>
    /// <para>
    /// El formato lo deciden los bytes del archivo, no su extension ni el Content-Type
    /// que declaro el cliente. Ver <c>ImageSignature</c>.
    /// </para>
    /// </remarks>
    [HttpPost("me/avatar")]
    [Authorize]
    [RequestSizeLimit(MaxAvatarBytes)]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return this.Problem(Error.Validation(
                "users.avatar_empty", "No llegó ningún archivo."));
        }

        await using var content = file.OpenReadStream();

        var upload = new AvatarUpload(file.FileName, file.Length, content);
        var result = await _users.UpdateAvatarAsync(upload, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Quita el avatar subido y vuelve a la inicial.</summary>
    [HttpDelete("me/avatar")]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken cancellationToken)
    {
        var result = await _users.RemoveAvatarAsync(cancellationToken);

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

    /// <summary>Empieza a seguir a un usuario. Idempotente. Devuelve su perfil actualizado.</summary>
    [HttpPost("{userName}/follow")]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Follow(string userName, CancellationToken cancellationToken)
    {
        var result = await _users.FollowAsync(userName, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Deja de seguir a un usuario. Idempotente.</summary>
    [HttpDelete("{userName}/follow")]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unfollow(string userName, CancellationToken cancellationToken)
    {
        var result = await _users.UnfollowAsync(userName, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Quienes siguen a este usuario.</summary>
    [HttpGet("{userName}/followers")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<FollowUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFollowers(
        string userName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _users.GetFollowersAsync(
            userName, new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>A quienes sigue este usuario.</summary>
    [HttpGet("{userName}/following")]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<FollowUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFollowing(
        string userName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _users.GetFollowingAsync(
            userName, new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
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
