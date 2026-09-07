using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Catalog.Dtos;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Proxy cacheado sobre MusicBrainz y Cover Art Archive.
/// Es publico: navegar el catalogo no requiere cuenta.
/// </summary>
[ApiController]
[Route("api/catalog")]
[AllowAnonymous]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Catalog)]
public class CatalogController : ControllerBase
{
    private readonly IMusicCatalogService _catalog;

    public CatalogController(IMusicCatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Busca artistas en MusicBrainz.</summary>
    [HttpGet("artists")]
    [ProducesResponseType<PagedResult<ArtistSearchItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchArtists(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _catalog.SearchArtistsAsync(
            query, new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Detalle de un artista. Lo cachea localmente en la primera consulta.</summary>
    [HttpGet("artists/{musicBrainzId:guid}")]
    [ProducesResponseType<ArtistDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArtist(Guid musicBrainzId, CancellationToken cancellationToken)
    {
        var result = await _catalog.GetArtistAsync(musicBrainzId.ToString(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Discografia de un artista (albumes y EPs).</summary>
    [HttpGet("artists/{musicBrainzId:guid}/albums")]
    [ProducesResponseType<PagedResult<AlbumSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArtistAlbums(
        Guid musicBrainzId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _catalog.GetArtistAlbumsAsync(
            musicBrainzId.ToString(), new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Busca albumes (release-groups) en MusicBrainz.</summary>
    [HttpGet("albums")]
    [ProducesResponseType<PagedResult<AlbumSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAlbums(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _catalog.SearchAlbumsAsync(
            query, new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Busca canciones (grabaciones) en MusicBrainz, agrupadas por tema.
    /// </summary>
    /// <remarks>
    /// Endpoint aparte y no un campo mas dentro de la busqueda de albumes: el buscador
    /// del frontend es uno solo, pero pide las tres cosas en paralelo y pinta cada
    /// seccion cuando llega. Devolverlas juntas obligaria a esperar a la mas lenta —y
    /// con una cola de 1 request por segundo contra MusicBrainz, la mas lenta llega
    /// tres turnos despues que la primera.
    /// </remarks>
    [HttpGet("songs")]
    [ProducesResponseType<PagedResult<SongSearchItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchSongs(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _catalog.SearchSongsAsync(
            query, new PageRequest(page, pageSize), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Detalle de un album con sus estadisticas de reviews.
    /// Lo cachea localmente (junto con su artista y portada) en la primera consulta.
    /// </summary>
    [HttpGet("albums/{musicBrainzId:guid}")]
    [ProducesResponseType<AlbumDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAlbum(Guid musicBrainzId, CancellationToken cancellationToken)
    {
        var result = await _catalog.GetAlbumAsync(musicBrainzId.ToString(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
