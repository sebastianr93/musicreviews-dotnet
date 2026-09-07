using Microsoft.EntityFrameworkCore;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Catalog.Dtos;
using MusicReviews.Application.Common.Exceptions;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.IntegrationTests.Infrastructure;

/// <summary>
/// Reemplaza al catalogo real durante los tests de integracion.
/// </summary>
/// <remarks>
/// Los tests de integracion no deben depender de MusicBrainz: seria una red externa
/// en el camino critico de la suite, con un limite de 1 request por segundo que la
/// volveria lentisima, resultados que cambian con el tiempo y fallos rojos cada vez
/// que el servicio tenga un mal dia. Lo que se esta probando aca es la Api propia,
/// no la integracion con un tercero.
///
/// El fake persiste artistas y albumes deterministas derivados del MBID pedido, que
/// es todo lo que necesitan reviews, comentarios y favoritos para funcionar.
/// </remarks>
internal sealed class FakeMusicCatalogService : IMusicCatalogService
{
    private readonly AppDbContext _context;

    public FakeMusicCatalogService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>MBID que el fake trata como inexistente, para poder probar el 404.</summary>
    public static readonly string UnknownMusicBrainzId = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// MBID con el que el fake simula que MusicBrainz no responde, para verificar
    /// que la caida de un tercero salga como 503 y no como 500.
    /// </summary>
    public static readonly string UnavailableMusicBrainzId = "11111111-1111-1111-1111-111111111111";

    public async Task<Result<ArtistDetailDto>> GetArtistAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        if (musicBrainzId == UnknownMusicBrainzId)
        {
            return Result.Failure<ArtistDetailDto>(
                Error.NotFound("catalog.artist_not_found", "No se encontro el artista."));
        }

        var artist = await GetOrCreateArtistAsync(musicBrainzId, cancellationToken);

        var favoriteCount = await _context.FavoriteArtists
            .CountAsync(f => f.ArtistId == artist.Id, cancellationToken);

        return Result.Success(new ArtistDetailDto(
            artist.Id, artist.MusicBrainzId, artist.Name, artist.Disambiguation,
            artist.Country, artist.Type, artist.ImageUrl, favoriteCount, artist.CachedAt));
    }

    public async Task<Result<AlbumDetailDto>> GetAlbumAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        if (musicBrainzId == UnavailableMusicBrainzId)
        {
            throw ExternalServiceUnavailableException.Timeout("MusicBrainz", TimeSpan.FromSeconds(15));
        }

        if (musicBrainzId == UnknownMusicBrainzId)
        {
            return Result.Failure<AlbumDetailDto>(
                Error.NotFound("catalog.album_not_found", "No se encontro el album."));
        }

        var album = await _context.Albums
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.MusicBrainzId == musicBrainzId, cancellationToken);

        if (album is null)
        {
            // Cada album de prueba cuelga de un artista propio derivado del mismo MBID.
            var artist = await GetOrCreateArtistAsync(ArtistMbidFor(musicBrainzId), cancellationToken);

            album = new Album
            {
                MusicBrainzId = musicBrainzId,
                Title = $"Album {musicBrainzId[..8]}",
                ArtistId = artist.Id,
                CoverArtUrl = $"https://example.test/cover/{musicBrainzId}.jpg",
                ReleaseDate = new DateOnly(2020, 1, 1),
                ReleaseDatePrecision = 3,
                PrimaryType = "Album",
                CachedAt = DateTimeOffset.UtcNow
            };

            _context.Albums.Add(album);
            await _context.SaveChangesAsync(cancellationToken);

            album.Artist = artist;
        }

        var stats = await _context.Reviews
            .Where(r => r.AlbumId == album.Id)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Average = (double?)g.Average(r => r.Score) })
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new AlbumDetailDto(
            album.Id, album.MusicBrainzId, album.Title, album.CoverArtUrl,
            album.ReleaseDate, album.PrimaryType,
            new ArtistSearchItemDto(
                album.Artist.Id, album.Artist.MusicBrainzId, album.Artist.Name,
                album.Artist.Disambiguation, album.Artist.Country, album.Artist.Type),
            stats?.Count ?? 0, stats?.Average, album.CachedAt));
    }

    public Task<Result<PagedResult<ArtistSearchItemDto>>> SearchArtistsAsync(
        string query, PageRequest page, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(PagedResult<ArtistSearchItemDto>.Empty(page.Page, page.PageSize)));

    public Task<Result<PagedResult<AlbumSummaryDto>>> SearchAlbumsAsync(
        string query, PageRequest page, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(PagedResult<AlbumSummaryDto>.Empty(page.Page, page.PageSize)));

    public Task<Result<PagedResult<SongSearchItemDto>>> SearchSongsAsync(
        string query, PageRequest page, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(PagedResult<SongSearchItemDto>.Empty(page.Page, page.PageSize)));

    public Task<Result<PagedResult<AlbumSummaryDto>>> GetArtistAlbumsAsync(
        string musicBrainzId, PageRequest page, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(PagedResult<AlbumSummaryDto>.Empty(page.Page, page.PageSize)));

    private async Task<Artist> GetOrCreateArtistAsync(string mbid, CancellationToken cancellationToken)
    {
        var artist = await _context.Artists
            .FirstOrDefaultAsync(a => a.MusicBrainzId == mbid, cancellationToken);

        if (artist is not null)
        {
            return artist;
        }

        artist = new Artist
        {
            MusicBrainzId = mbid,
            Name = $"Artista {mbid[..8]}",
            Type = "Group",
            CachedAt = DateTimeOffset.UtcNow
        };

        _context.Artists.Add(artist);
        await _context.SaveChangesAsync(cancellationToken);

        return artist;
    }

    /// <summary>
    /// Deriva un MBID de artista a partir del de album cambiando el primer caracter,
    /// para que cada album de prueba tenga su propio artista sin colisionar.
    /// </summary>
    private static string ArtistMbidFor(string albumMbid) =>
        (albumMbid[0] == 'a' ? 'b' : 'a') + albumMbid[1..];
}
