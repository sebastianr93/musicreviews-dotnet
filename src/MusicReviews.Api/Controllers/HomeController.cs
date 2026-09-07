using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Home;
using MusicReviews.Application.Home.Dtos;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Las secciones del home.
/// </summary>
/// <remarks>
/// Publicas: un visitante sin cuenta tiene que poder llegar, ver que hay y engancharse.
/// Con sesion la respuesta cambia —explorar deja de proponerle lo que ya reseñó—, y
/// por eso los endpoints leen el usuario actual aunque no lo exijan.
/// La tercera seccion del home es <c>GET /api/feed</c>, que ya existe y si pide sesion.
/// </remarks>
[ApiController]
[Route("api/home")]
[AllowAnonymous]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class HomeController : ControllerBase
{
    private readonly IHomeService _home;

    public HomeController(IHomeService home)
    {
        _home = home;
    }

    /// <summary>Albumes con mas movimiento en los ultimos dias.</summary>
    /// <param name="windowDays">Ventana de actividad. Se recorta al rango permitido.</param>
    [HttpGet("popular")]
    [ProducesResponseType<PagedResult<HomeAlbumDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPopular(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] int windowDays = HomeDefaults.PopularWindowDays,
        CancellationToken cancellationToken = default)
    {
        var result = await _home.GetPopularAsync(
            new PageRequest(page, pageSize), windowDays, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Albumes del catalogo para descubrir.</summary>
    [HttpGet("explore")]
    [ProducesResponseType<PagedResult<HomeAlbumDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExplore(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _home.GetExploreAsync(new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
