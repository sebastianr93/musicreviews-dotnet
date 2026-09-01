using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Catalog.Dtos;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Catalog.Contracts;
using MusicReviews.Infrastructure.Persistence;
using Npgsql;

namespace MusicReviews.Infrastructure.Catalog;

/// <inheritdoc cref="IMusicCatalogService"/>
internal sealed class MusicCatalogService : IMusicCatalogService
{
    /// <summary>SQLSTATE de violacion de constraint unico en PostgreSQL.</summary>
    private const string UniqueViolationSqlState = "23505";

    private readonly AppDbContext _context;
    private readonly IMusicBrainzClient _musicBrainz;
    private readonly ICoverArtArchiveClient _coverArt;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MusicBrainzOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MusicCatalogService> _logger;

    public MusicCatalogService(
        AppDbContext context,
        IMusicBrainzClient musicBrainz,
        ICoverArtArchiveClient coverArt,
        IMemoryCache cache,
        IOptionsMonitor<MusicBrainzOptions> options,
        TimeProvider timeProvider,
        ILogger<MusicCatalogService> logger)
    {
        _context = context;
        _musicBrainz = musicBrainz;
        _coverArt = coverArt;
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Busquedas
    // ------------------------------------------------------------------

    public async Task<Result<PagedResult<ArtistSearchItemDto>>> SearchArtistsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Failure<PagedResult<ArtistSearchItemDto>>(
                Error.Validation("catalog.empty_query", "La busqueda no puede estar vacia."));
        }

        var cacheKey = $"mb:artists:{query.Trim().ToLowerInvariant()}:{page.Page}:{page.PageSize}";

        var response = await GetOrCreateSearchAsync(
            cacheKey,
            ct => _musicBrainz.SearchArtistsAsync(query.Trim(), page.PageSize, page.Offset, ct),
            cancellationToken);

        if (response is null)
        {
            return Result.Success(PagedResult<ArtistSearchItemDto>.Empty(page.Page, page.PageSize));
        }

        // Una sola query para saber cuales de los resultados ya estan cacheados:
        // sin esto seria un SELECT por artista devuelto.
        var localIds = await GetLocalArtistIdsAsync(
            response.Artists.Select(a => a.Id),
            cancellationToken);

        var items = response.Artists
            .Select(a => new ArtistSearchItemDto(
                localIds.GetValueOrDefault(a.Id),
                a.Id,
                a.Name,
                NullIfEmpty(a.Disambiguation),
                NullIfEmpty(a.Country),
                NullIfEmpty(a.Type)))
            .ToList();

        return Result.Success(new PagedResult<ArtistSearchItemDto>(
            items, page.Page, page.PageSize, response.Count));
    }

    public async Task<Result<PagedResult<AlbumSummaryDto>>> SearchAlbumsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Failure<PagedResult<AlbumSummaryDto>>(
                Error.Validation("catalog.empty_query", "La busqueda no puede estar vacia."));
        }

        var cacheKey = $"mb:albums:{query.Trim().ToLowerInvariant()}:{page.Page}:{page.PageSize}";

        var response = await GetOrCreateSearchAsync(
            cacheKey,
            ct => _musicBrainz.SearchReleaseGroupsAsync(query.Trim(), page.PageSize, page.Offset, ct),
            cancellationToken);

        if (response is null)
        {
            return Result.Success(PagedResult<AlbumSummaryDto>.Empty(page.Page, page.PageSize));
        }

        var items = await MapReleaseGroupsAsync(response.ReleaseGroups, cancellationToken);

        return Result.Success(new PagedResult<AlbumSummaryDto>(
            items, page.Page, page.PageSize, response.Count));
    }

    // ------------------------------------------------------------------
    // Detalles (cache-first sobre Postgres)
    // ------------------------------------------------------------------

    public async Task<Result<ArtistDetailDto>> GetArtistAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        var artist = await GetOrCacheArtistAsync(musicBrainzId, cancellationToken);

        if (artist is null)
        {
            return Result.Failure<ArtistDetailDto>(
                Error.NotFound("catalog.artist_not_found", "No se encontro el artista."));
        }

        var favoriteCount = await _context.FavoriteArtists
            .CountAsync(f => f.ArtistId == artist.Id, cancellationToken);

        return Result.Success(new ArtistDetailDto(
            artist.Id,
            artist.MusicBrainzId,
            artist.Name,
            artist.Disambiguation,
            artist.Country,
            artist.Type,
            artist.ImageUrl,
            favoriteCount,
            artist.CachedAt));
    }

    public async Task<Result<PagedResult<AlbumSummaryDto>>> GetArtistAlbumsAsync(
        string musicBrainzId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var artist = await GetOrCacheArtistAsync(musicBrainzId, cancellationToken);

        if (artist is null)
        {
            return Result.Failure<PagedResult<AlbumSummaryDto>>(
                Error.NotFound("catalog.artist_not_found", "No se encontro el artista."));
        }

        var cacheKey = $"mb:artist-albums:{musicBrainzId}:{page.Page}:{page.PageSize}";

        var response = await GetOrCreateSearchAsync(
            cacheKey,
            ct => _musicBrainz.BrowseArtistReleaseGroupsAsync(musicBrainzId, page.PageSize, page.Offset, ct),
            cancellationToken);

        if (response is null)
        {
            return Result.Success(PagedResult<AlbumSummaryDto>.Empty(page.Page, page.PageSize));
        }

        var items = await MapReleaseGroupsAsync(
            response.ReleaseGroups,
            cancellationToken,
            fallbackArtistName: artist.Name,
            fallbackArtistMbid: artist.MusicBrainzId);

        return Result.Success(new PagedResult<AlbumSummaryDto>(
            items, page.Page, page.PageSize, response.Count));
    }

    public async Task<Result<AlbumDetailDto>> GetAlbumAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        var album = await GetOrCacheAlbumAsync(musicBrainzId, cancellationToken);

        if (album is null)
        {
            return Result.Failure<AlbumDetailDto>(
                Error.NotFound("catalog.album_not_found", "No se encontro el album."));
        }

        // Conteo y promedio en una sola consulta agregada: no se traen las reviews
        // para contarlas en memoria.
        var stats = await _context.Reviews
            .Where(r => r.AlbumId == album.Id)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Average = (double?)g.Average(r => r.Score) })
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new AlbumDetailDto(
            album.Id,
            album.MusicBrainzId,
            album.Title,
            album.CoverArtUrl,
            album.ReleaseDate,
            album.PrimaryType,
            new ArtistSearchItemDto(
                album.Artist.Id,
                album.Artist.MusicBrainzId,
                album.Artist.Name,
                album.Artist.Disambiguation,
                album.Artist.Country,
                album.Artist.Type),
            stats?.Count ?? 0,
            stats?.Average,
            album.CachedAt));
    }

    // ------------------------------------------------------------------
    // Cache local
    // ------------------------------------------------------------------

    private async Task<Artist?> GetOrCacheArtistAsync(string mbid, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var staleBefore = now.AddDays(-_options.CurrentValue.CacheDays);

        var local = await _context.Artists
            .FirstOrDefaultAsync(a => a.MusicBrainzId == mbid, ct);

        if (local is not null && local.CachedAt > staleBefore)
        {
            return local;
        }

        var remote = await _musicBrainz.GetArtistAsync(mbid, ct);

        if (remote is null)
        {
            // Si MusicBrainz no lo conoce pero lo teniamos cacheado, se devuelve
            // lo viejo antes que un 404: es preferible un dato desactualizado a
            // perder el artista porque la fuente externa fallo o lo fusiono.
            return local;
        }

        return await UpsertArtistAsync(remote, local, now, ct);
    }

    private async Task<Artist> UpsertArtistAsync(MbArtist remote, Artist? local, DateTimeOffset now, CancellationToken ct)
    {
        if (local is not null)
        {
            ApplyArtist(local, remote, now);
            await _context.SaveChangesAsync(ct);
            return local;
        }

        var created = new Artist { MusicBrainzId = remote.Id };
        ApplyArtist(created, remote, now);

        _context.Artists.Add(created);

        try
        {
            await _context.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dos requests pidieron el mismo artista a la vez y ambos vieron la cache vacia.
            // El indice unico de MusicBrainzId corta al segundo: se descarta el insert y
            // se lee el que gano.
            _logger.LogDebug("Insercion concurrente de artista {Mbid}; se reutiliza la existente.", remote.Id);

            _context.Entry(created).State = EntityState.Detached;

            return await _context.Artists.FirstAsync(a => a.MusicBrainzId == remote.Id, ct);
        }
    }

    private static void ApplyArtist(Artist artist, MbArtist remote, DateTimeOffset now)
    {
        artist.Name = remote.Name;
        artist.Disambiguation = NullIfEmpty(remote.Disambiguation);
        artist.Country = NullIfEmpty(remote.Country);
        artist.Type = NullIfEmpty(remote.Type);
        artist.CachedAt = now;
    }

    private async Task<Album?> GetOrCacheAlbumAsync(string mbid, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var staleBefore = now.AddDays(-_options.CurrentValue.CacheDays);

        var local = await _context.Albums
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.MusicBrainzId == mbid, ct);

        if (local is not null && local.CachedAt > staleBefore)
        {
            return local;
        }

        var remote = await _musicBrainz.GetReleaseGroupAsync(mbid, ct);

        if (remote is null)
        {
            return local;
        }

        var remoteArtist = remote.ArtistCredit.FirstOrDefault()?.Artist;

        if (remoteArtist is null)
        {
            _logger.LogWarning("El release-group {Mbid} no trae artist-credit; no se puede cachear.", mbid);
            return local;
        }

        var artist = await GetOrCacheArtistAsync(remoteArtist.Id, ct)
            ?? await UpsertArtistAsync(remoteArtist, null, now, ct);

        var coverUrl = await _coverArt.GetReleaseGroupCoverUrlAsync(mbid, ct);
        var (releaseDate, precision) = PartialDate.Parse(remote.FirstReleaseDate);

        if (local is not null)
        {
            ApplyAlbum(local, remote, artist.Id, coverUrl, releaseDate, precision, now);
            await _context.SaveChangesAsync(ct);
            local.Artist = artist;
            return local;
        }

        var created = new Album { MusicBrainzId = remote.Id };
        ApplyAlbum(created, remote, artist.Id, coverUrl, releaseDate, precision, now);

        _context.Albums.Add(created);

        try
        {
            await _context.SaveChangesAsync(ct);
            created.Artist = artist;
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogDebug("Insercion concurrente de album {Mbid}; se reutiliza la existente.", remote.Id);

            _context.Entry(created).State = EntityState.Detached;

            return await _context.Albums
                .Include(a => a.Artist)
                .FirstAsync(a => a.MusicBrainzId == remote.Id, ct);
        }
    }

    private static void ApplyAlbum(
        Album album,
        MbReleaseGroup remote,
        int artistId,
        string? coverUrl,
        DateOnly? releaseDate,
        int precision,
        DateTimeOffset now)
    {
        album.Title = remote.Title;
        album.ArtistId = artistId;
        album.PrimaryType = NullIfEmpty(remote.PrimaryType);
        album.ReleaseDate = releaseDate;
        album.ReleaseDatePrecision = precision;
        album.CachedAt = now;

        // Si Cover Art Archive no respondio, se conserva la portada previa en vez
        // de borrarla por un fallo transitorio.
        if (coverUrl is not null)
        {
            album.CoverArtUrl = coverUrl;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Cachea en memoria el resultado crudo de una busqueda. Sin esto, cada tecla que
    /// el usuario escribe en el buscador consumiria un turno de la cola de 1 req/seg.
    /// </summary>
    private async Task<T?> GetOrCreateSearchAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken ct)
        where T : class
    {
        if (_cache.TryGetValue(cacheKey, out T? cached))
        {
            return cached;
        }

        T? response;

        try
        {
            response = await factory(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Fallo la consulta a MusicBrainz para {CacheKey}", cacheKey);
            throw;
        }

        if (response is not null)
        {
            _cache.Set(
                cacheKey,
                response,
                TimeSpan.FromMinutes(_options.CurrentValue.SearchCacheMinutes));
        }

        return response;
    }

    /// <summary>
    /// Mapea release-groups a DTOs resolviendo en UNA sola query cuales ya estan
    /// cacheados localmente (y su portada guardada).
    /// </summary>
    private async Task<List<AlbumSummaryDto>> MapReleaseGroupsAsync(
        IReadOnlyList<MbReleaseGroup> releaseGroups,
        CancellationToken ct,
        string? fallbackArtistName = null,
        string? fallbackArtistMbid = null)
    {
        var mbids = releaseGroups.Select(rg => rg.Id).Distinct().ToArray();

        var localAlbums = await _context.Albums
            .Where(a => mbids.Contains(a.MusicBrainzId))
            .Select(a => new { a.Id, a.MusicBrainzId, a.CoverArtUrl })
            .ToDictionaryAsync(a => a.MusicBrainzId, ct);

        var items = new List<AlbumSummaryDto>(releaseGroups.Count);

        foreach (var rg in releaseGroups)
        {
            var credit = rg.ArtistCredit.FirstOrDefault();
            var (releaseDate, _) = PartialDate.Parse(rg.FirstReleaseDate);

            localAlbums.TryGetValue(rg.Id, out var local);

            items.Add(new AlbumSummaryDto(
                local?.Id,
                rg.Id,
                rg.Title,
                // La portada solo se conoce si el album ya fue cacheado: resolverla aca
                // implicaria un HEAD a Cover Art Archive por cada fila del listado.
                local?.CoverArtUrl,
                releaseDate,
                NullIfEmpty(rg.PrimaryType),
                credit?.Artist?.Name ?? credit?.Name ?? fallbackArtistName ?? "Desconocido",
                credit?.Artist?.Id ?? fallbackArtistMbid ?? string.Empty));
        }

        return items;
    }

    private async Task<Dictionary<string, int>> GetLocalArtistIdsAsync(
        IEnumerable<string> mbids,
        CancellationToken ct)
    {
        var ids = mbids.Distinct().ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await _context.Artists
            .Where(a => ids.Contains(a.MusicBrainzId))
            .Select(a => new { a.MusicBrainzId, a.Id })
            .ToDictionaryAsync(a => a.MusicBrainzId, a => a.Id, ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
