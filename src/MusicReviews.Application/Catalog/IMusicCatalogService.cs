using MusicReviews.Application.Catalog.Dtos;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Application.Catalog;

/// <summary>
/// Catalogo musical: proxy cacheado sobre MusicBrainz + Cover Art Archive.
/// </summary>
/// <remarks>
/// Las busquedas van siempre contra MusicBrainz (con cache en memoria de corta vida)
/// porque la cache local solo contiene lo que alguien ya consulto. Los detalles, en
/// cambio, son cache-first contra Postgres: la primera consulta trae y persiste,
/// las siguientes se sirven de la base hasta que el registro queda viejo.
/// </remarks>
public interface IMusicCatalogService
{
    Task<Result<PagedResult<ArtistSearchItemDto>>> SearchArtistsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<AlbumSummaryDto>>> SearchAlbumsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Detalle de artista. Lo cachea localmente si no estaba.</summary>
    Task<Result<ArtistDetailDto>> GetArtistAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default);

    /// <summary>Discografia de un artista (release-groups de tipo Album y EP).</summary>
    Task<Result<PagedResult<AlbumSummaryDto>>> GetArtistAlbumsAsync(
        string musicBrainzId,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Detalle de album con estadisticas de reviews. Lo cachea localmente si no estaba.</summary>
    Task<Result<AlbumDetailDto>> GetAlbumAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default);
}
