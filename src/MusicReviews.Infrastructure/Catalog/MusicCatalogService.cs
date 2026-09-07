using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Catalog.Dtos;
using MusicReviews.Application.Common.Exceptions;
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
    private readonly IWikidataClient _wikidata;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MusicBrainzOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MusicCatalogService> _logger;

    public MusicCatalogService(
        AppDbContext context,
        IMusicBrainzClient musicBrainz,
        ICoverArtArchiveClient coverArt,
        IWikidataClient wikidata,
        IMemoryCache cache,
        IOptionsMonitor<MusicBrainzOptions> options,
        TimeProvider timeProvider,
        ILogger<MusicCatalogService> logger)
    {
        _context = context;
        _musicBrainz = musicBrainz;
        _coverArt = coverArt;
        _wikidata = wikidata;
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Busquedas
    // ------------------------------------------------------------------

    public Task<Result<PagedResult<ArtistSearchItemDto>>> SearchArtistsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        GuardAsync(() => SearchArtistsCoreAsync(query, page, cancellationToken));

    private async Task<Result<PagedResult<ArtistSearchItemDto>>> SearchArtistsCoreAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Failure<PagedResult<ArtistSearchItemDto>>(
                Error.Validation("catalog.empty_query", "La búsqueda no puede estar vacía."));
        }

        var fetchLimit = _options.CurrentValue.SearchFetchLimit;

        // La clave NO incluye la pagina: se trae un lote grande una sola vez y las
        // paginas se sirven de el. Paginar deja de costar un turno de la cola.
        var cacheKey = $"mb:artists:{query.Trim().ToLowerInvariant()}:{fetchLimit}";

        var (response, fromNetwork) = await GetOrCreateSearchAsync(
            cacheKey,
            ct => _musicBrainz.SearchArtistsAsync(query.Trim(), fetchLimit, 0, ct),
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

        var ranked = AlbumSearchRanking.RankArtists(response.Artists
            .Select(a => new ArtistSearchItemDto(
                localIds.GetValueOrDefault(a.Id),
                a.Id,
                a.Name,
                NullIfEmpty(a.Disambiguation),
                NullIfEmpty(a.Country),
                NullIfEmpty(a.Type),
                a.Score)));

        if (fromNetwork)
        {
            await SeedArtistsAsync(response.Artists, cancellationToken);
        }

        return Result.Success(Paginate(ranked, page));
    }

    public Task<Result<PagedResult<AlbumSummaryDto>>> SearchAlbumsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        GuardAsync(() => SearchAlbumsCoreAsync(query, page, cancellationToken));

    private async Task<Result<PagedResult<AlbumSummaryDto>>> SearchAlbumsCoreAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Failure<PagedResult<AlbumSummaryDto>>(
                Error.Validation("catalog.empty_query", "La búsqueda no puede estar vacía."));
        }

        var fetchLimit = _options.CurrentValue.SearchFetchLimit;
        var cacheKey = $"mb:albums:{query.Trim().ToLowerInvariant()}:{fetchLimit}";

        var (response, fromNetwork) = await GetOrCreateSearchAsync(
            cacheKey,
            ct => _musicBrainz.SearchReleaseGroupsAsync(query.Trim(), fetchLimit, 0, ct),
            cancellationToken);

        if (response is null)
        {
            return Result.Success(PagedResult<AlbumSummaryDto>.Empty(page.Page, page.PageSize));
        }

        var mapped = await MapReleaseGroupsAsync(response.ReleaseGroups, cancellationToken);

        // El orden de MusicBrainz es por relevancia textual y no distingue entre
        // homonimos. Ver AlbumSearchRanking.
        var ranked = AlbumSearchRanking.Rank(mapped);

        if (fromNetwork)
        {
            await SeedAlbumsAsync(response.ReleaseGroups, ranked, cancellationToken);
        }

        return Result.Success(Paginate(ranked, page));
    }

    public Task<Result<PagedResult<SongSearchItemDto>>> SearchSongsAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        GuardAsync(() => SearchSongsCoreAsync(query, page, cancellationToken));

    private async Task<Result<PagedResult<SongSearchItemDto>>> SearchSongsCoreAsync(
        string query,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result.Failure<PagedResult<SongSearchItemDto>>(
                Error.Validation("catalog.empty_query", "La búsqueda no puede estar vacía."));
        }

        var fetchLimit = _options.CurrentValue.SearchFetchLimit;
        var cacheKey = $"mb:songs:{query.Trim().ToLowerInvariant()}:{fetchLimit}";

        var (response, _) = await GetOrCreateSearchAsync(
            cacheKey,
            ct => _musicBrainz.SearchRecordingsAsync(query.Trim(), fetchLimit, 0, ct),
            cancellationToken);

        if (response is null)
        {
            return Result.Success(PagedResult<SongSearchItemDto>.Empty(page.Page, page.PageSize));
        }

        var mapped = await MapRecordingsAsync(response.Recordings, cancellationToken);

        // El agrupado va DESPUES del mapeo y ANTES de paginar: agrupar sobre la pagina
        // ya recortada dejaria pasar duplicados de la misma cancion en paginas distintas.
        var grouped = SongSearchGrouping.GroupAndRank(mapped);

        return Result.Success(Paginate(grouped, page));
    }

    // ------------------------------------------------------------------
    // Siembra del catalogo local
    // ------------------------------------------------------------------

    /// <summary>
    /// Cuantos resultados de una busqueda se persisten. Suficiente para que el
    /// desplegable tenga con que trabajar; no tantos como para convertir cada busqueda
    /// en una carga masiva.
    /// </summary>
    private const int SeedLimit = 20;

    /// <summary>
    /// Guarda en el catalogo local los mejores resultados de una busqueda.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Para que.</b> El desplegable de busqueda instantanea consulta solo Postgres,
    /// porque MusicBrainz admite 1 request por segundo y eso no da para un typeahead.
    /// Si el catalogo local creciera unicamente cuando alguien abre el detalle de un
    /// album, el desplegable estaria vacio durante semanas. Sembrando, cada busqueda
    /// que alguien hace deja el indice un poco mas util para el siguiente.
    /// </para>
    /// <para>
    /// <b>Las filas sembradas son parciales</b> y llevan <c>CachedAt</c> en el epoch a
    /// proposito: no pasaron por el lookup de detalle, asi que les falta lo que solo
    /// llega ahi. Con esa fecha quedan siempre "vencidas", y la primera vez que alguien
    /// abra el album o el artista se refrescan solas contra MusicBrainz. No hace falta
    /// una columna de "esto es un esbozo": la fecha ya lo dice.
    /// </para>
    /// <para>
    /// <b>Nada de esto puede romper la busqueda.</b> Sembrar es un efecto secundario
    /// util, no el trabajo que el usuario pidio: si falla, se registra y se devuelven
    /// igual los resultados.
    /// </para>
    /// </remarks>
    private async Task SeedAlbumsAsync(
        IReadOnlyList<MbReleaseGroup> remote,
        IReadOnlyList<AlbumSummaryDto> ranked,
        CancellationToken ct)
    {
        try
        {
            // Se siembran los mejores segun NUESTRO orden, no los primeros que devolvio
            // MusicBrainz: el orden de la API no distingue entre homonimos, que es todo
            // el problema que resuelve AlbumSearchRanking.
            var top = ranked.Take(SeedLimit).Select(a => a.MusicBrainzId).ToHashSet(StringComparer.Ordinal);
            var groups = remote.Where(rg => top.Contains(rg.Id)).ToList();

            if (groups.Count == 0)
            {
                return;
            }

            var creditos = groups
                .Select(rg => rg.ArtistCredit.FirstOrDefault()?.Artist)
                .OfType<MbArtist>()
                .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                .Select(a => (a.Id, (string?)a.Name));

            var artistIds = await EnsureArtistStubsAsync(creditos, ct);

            var mbids = groups.Select(rg => rg.Id).ToArray();

            var existing = (await _context.Albums
                    .Where(a => mbids.Contains(a.MusicBrainzId))
                    .Select(a => a.MusicBrainzId)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var nuevos = new List<Album>();

            foreach (var rg in groups)
            {
                var artistMbid = rg.ArtistCredit.FirstOrDefault()?.Artist?.Id;

                if (existing.Contains(rg.Id)
                    || artistMbid is null
                    || !artistIds.TryGetValue(artistMbid, out var artistId))
                {
                    continue;
                }

                var (releaseDate, precision) = PartialDate.Parse(rg.FirstReleaseDate);

                nuevos.Add(new Album
                {
                    MusicBrainzId = rg.Id,
                    Title = rg.Title,
                    ArtistId = artistId,
                    PrimaryType = NullIfEmpty(rg.PrimaryType),
                    ReleaseDate = releaseDate,
                    ReleaseDatePrecision = precision,
                    CoverArtUrl = CoverUrlFor(rg.Id),
                    CachedAt = DateTimeOffset.UnixEpoch
                });
            }

            await SaveSeedAsync(nuevos, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo sembrar el catalogo con los albumes de la busqueda.");
            _context.ChangeTracker.Clear();
        }
    }

    private async Task SeedArtistsAsync(IReadOnlyList<MbArtist> remote, CancellationToken ct)
    {
        try
        {
            await EnsureArtistStubsAsync(
                remote.Take(SeedLimit)
                    .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                    .Select(a => (a.Id, (string?)a.Name)),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo sembrar el catalogo con los artistas de la busqueda.");
            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Inserta los artistas que falten y devuelve el mapa MBID → Id local de TODOS los
    /// pedidos, existieran ya o no.
    /// </summary>
    private async Task<Dictionary<string, int>> EnsureArtistStubsAsync(
        IEnumerable<(string Mbid, string? Name)> artists,
        CancellationToken ct)
    {
        var unicos = artists
            .Where(a => !string.IsNullOrWhiteSpace(a.Mbid))
            .GroupBy(a => a.Mbid, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (unicos.Count == 0)
        {
            return [];
        }

        var mbids = unicos.Select(a => a.Mbid).ToArray();

        var existentes = await _context.Artists
            .Where(a => mbids.Contains(a.MusicBrainzId))
            .Select(a => new { a.MusicBrainzId, a.Id })
            .ToDictionaryAsync(a => a.MusicBrainzId, a => a.Id, ct);

        var nuevos = unicos
            .Where(a => !existentes.ContainsKey(a.Mbid))
            .Select(a => new Artist
            {
                MusicBrainzId = a.Mbid,
                Name = NullIfEmpty(a.Name) ?? "Desconocido",
                CachedAt = DateTimeOffset.UnixEpoch
            })
            .ToList();

        await SaveSeedAsync(nuevos, ct);

        if (nuevos.Count > 0)
        {
            // Se releen en vez de usar los Id de las entidades insertadas: si otra
            // request gano la carrera, las nuestras quedaron descartadas y los Id buenos
            // son los de la base.
            var recien = await _context.Artists
                .Where(a => mbids.Contains(a.MusicBrainzId))
                .Select(a => new { a.MusicBrainzId, a.Id })
                .ToDictionaryAsync(a => a.MusicBrainzId, a => a.Id, ct);

            return recien;
        }

        return existentes;
    }

    /// <summary>
    /// Guarda las filas sembradas tolerando que otra request haya insertado las mismas.
    /// </summary>
    private async Task SaveSeedAsync<T>(List<T> nuevos, CancellationToken ct)
        where T : class
    {
        if (nuevos.Count == 0)
        {
            return;
        }

        _context.Set<T>().AddRange(nuevos);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dos personas buscaron lo mismo al mismo tiempo. El indice unico de
            // MusicBrainzId corta al segundo y no hay nada que arreglar: la fila que
            // importa ya esta.
            _logger.LogDebug("Siembra concurrente; se descartan las filas duplicadas.");
            _context.ChangeTracker.Clear();
        }
    }

    // ------------------------------------------------------------------
    // Detalles (cache-first sobre Postgres)
    // ------------------------------------------------------------------

    public Task<Result<ArtistDetailDto>> GetArtistAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default) =>
        GuardAsync(() => GetArtistCoreAsync(musicBrainzId, cancellationToken));

    private async Task<Result<ArtistDetailDto>> GetArtistCoreAsync(
        string musicBrainzId,
        CancellationToken cancellationToken)
    {
        var artist = await GetOrCacheArtistAsync(musicBrainzId, cancellationToken);

        if (artist is null)
        {
            return Result.Failure<ArtistDetailDto>(
                Error.NotFound("catalog.artist_not_found", "No se encontró el artista."));
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

    public Task<Result<PagedResult<AlbumSummaryDto>>> GetArtistAlbumsAsync(
        string musicBrainzId,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        GuardAsync(() => GetArtistAlbumsCoreAsync(musicBrainzId, page, cancellationToken));

    private async Task<Result<PagedResult<AlbumSummaryDto>>> GetArtistAlbumsCoreAsync(
        string musicBrainzId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var artist = await GetOrCacheArtistAsync(musicBrainzId, cancellationToken);

        if (artist is null)
        {
            return Result.Failure<PagedResult<AlbumSummaryDto>>(
                Error.NotFound("catalog.artist_not_found", "No se encontró el artista."));
        }

        var cacheKey = $"mb:artist-albums:{musicBrainzId}:{page.Page}:{page.PageSize}";

        var (response, _) = await GetOrCreateSearchAsync(
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

    public Task<Result<AlbumDetailDto>> GetAlbumAsync(
        string musicBrainzId,
        CancellationToken cancellationToken = default) =>
        GuardAsync(() => GetAlbumCoreAsync(musicBrainzId, cancellationToken));

    private async Task<Result<AlbumDetailDto>> GetAlbumCoreAsync(
        string musicBrainzId,
        CancellationToken cancellationToken)
    {
        var album = await GetOrCacheAlbumAsync(musicBrainzId, cancellationToken);

        if (album is null)
        {
            return Result.Failure<AlbumDetailDto>(
                Error.NotFound("catalog.album_not_found", "No se encontró el álbum."));
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

        var imageUrl = await ResolveArtistImageAsync(remote, local?.ImageUrl, ct);

        return await UpsertArtistAsync(remote, local, imageUrl, now, ct);
    }

    /// <summary>
    /// Busca la foto del artista siguiendo la relacion a Wikidata.
    /// </summary>
    /// <remarks>
    /// Si no hay relacion, o Wikidata no responde, o el elemento no tiene foto, se
    /// conserva la que ya estaba: la foto es un adorno y perderla porque un tercero tuvo
    /// un mal momento seria peor que mostrar una vieja.
    /// </remarks>
    private async Task<string?> ResolveArtistImageAsync(
        MbArtist remote,
        string? existing,
        CancellationToken ct)
    {
        var entityId = WikidataEntityId(remote);

        if (entityId is null)
        {
            return existing;
        }

        return await _wikidata.GetImageUrlAsync(entityId, ct) ?? existing;
    }

    /// <summary>
    /// Extrae el identificador de Wikidata (Q...) de las relaciones del artista.
    /// </summary>
    private static string? WikidataEntityId(MbArtist remote)
    {
        var url = remote.Relations
            .FirstOrDefault(r => string.Equals(r.Type, "wikidata", StringComparison.OrdinalIgnoreCase))
            ?.Url?.Resource;

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        // La relacion viene como "https://www.wikidata.org/wiki/Q11649": el id es el
        // ultimo segmento. Se valida la forma para no mandar cualquier cosa a la API.
        var id = url.TrimEnd('/').Split('/')[^1];

        return id.Length > 1 && id[0] == 'Q' && id[1..].All(char.IsAsciiDigit) ? id : null;
    }

    private async Task<Artist> UpsertArtistAsync(
        MbArtist remote,
        Artist? local,
        string? imageUrl,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (local is not null)
        {
            ApplyArtist(local, remote, imageUrl, now);
            await _context.SaveChangesAsync(ct);
            return local;
        }

        var created = new Artist { MusicBrainzId = remote.Id };
        ApplyArtist(created, remote, imageUrl, now);

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

    private static void ApplyArtist(Artist artist, MbArtist remote, string? imageUrl, DateTimeOffset now)
    {
        artist.Name = remote.Name;
        artist.Disambiguation = NullIfEmpty(remote.Disambiguation);
        artist.Country = NullIfEmpty(remote.Country);
        artist.Type = NullIfEmpty(remote.Type);
        artist.CachedAt = now;

        // Mismo criterio que con la portada del album: si no se pudo resolver, se
        // conserva la que estaba en vez de borrarla por un fallo pasajero.
        if (imageUrl is not null)
        {
            artist.ImageUrl = imageUrl;
        }
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

        // Sin foto: el artist-credit del release-group no trae relaciones, asi que aca
        // no hay de donde sacarla. Se resuelve cuando alguien abra la ficha del artista.
        var artist = await GetOrCacheArtistAsync(remoteArtist.Id, ct)
            ?? await UpsertArtistAsync(remoteArtist, null, imageUrl: null, now, ct);

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
    /// <summary>
    /// Pagina en memoria sobre el lote ya traido y reordenado.
    /// </summary>
    /// <remarks>
    /// El total reportado es el del lote, no el que dice MusicBrainz. Es a proposito:
    /// solo se puede paginar sobre lo que se trajo, y anunciar 3.000 resultados para
    /// que la pagina 10 venga vacia es peor que anunciar los 100 que si estan.
    /// </remarks>
    private static PagedResult<T> Paginate<T>(IReadOnlyList<T> all, PageRequest page)
    {
        var items = all.Skip(page.Offset).Take(page.PageSize).ToList();

        return new PagedResult<T>(items, page.Page, page.PageSize, all.Count);
    }

    /// <summary>
    /// Traduce la caida de un servicio externo a un resultado de negocio.
    /// </summary>
    /// <remarks>
    /// La excepcion viaja desde el cliente HTTP hasta aca y no mas arriba: los
    /// llamadores internos (crear una review, marcar un favorito) trabajan con
    /// <see cref="Result"/>, y que un fallo del catalogo llegara como excepcion
    /// los obligaria a mezclar dos estilos de manejo de error para el mismo caso.
    /// </remarks>
    private async Task<Result<T>> GuardAsync<T>(Func<Task<Result<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ExternalServiceUnavailableException ex)
        {
            _logger.LogWarning(
                "El catalogo externo no esta disponible: {Message}", ex.Message);

            return Result.Failure<T>(Error.Unavailable(
                "catalog.unavailable",
                "El catálogo de música no está respondiendo en este momento. " +
                "Volvé a intentar en unos segundos."));
        }
    }

    /// <summary>
    /// Cachea en memoria el resultado crudo de una busqueda y avisa si tuvo que salir a
    /// la red.
    /// </summary>
    /// <remarks>
    /// El segundo dato importa para la siembra del catalogo: sin el, cada pagina de la
    /// misma busqueda —que se sirve de esta cache— volveria a intentar persistir lo
    /// mismo, y paginar dejaria de ser gratis.
    /// </remarks>
    private async Task<(T? Response, bool FromNetwork)> GetOrCreateSearchAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken ct)
        where T : class
    {
        if (_cache.TryGetValue(cacheKey, out T? cached))
        {
            return (cached, false);
        }

        var response = await factory(ct);

        if (response is not null)
        {
            _cache.Set(
                cacheKey,
                response,
                TimeSpan.FromMinutes(_options.CurrentValue.SearchCacheMinutes));
        }

        return (response, true);
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
            var (releaseDate, _) = PartialDate.Parse(rg.FirstReleaseDate);

            localAlbums.TryGetValue(rg.Id, out var local);

            // El credito completo, no solo el primero: un split o un colaborativo
            // ("Jay-Z & Linkin Park") quedaria atribuido al artista equivocado.
            var creditName = rg.ArtistCredit.FormatCredit();

            items.Add(new AlbumSummaryDto(
                local?.Id,
                rg.Id,
                rg.Title,
                // Si el album ya esta cacheado se usa la portada verificada; si no, se
                // emite la URL deterministica y el navegador resuelve. Ver CoverArt.
                local?.CoverArtUrl ?? CoverUrlFor(rg.Id),
                releaseDate,
                NullIfEmpty(rg.PrimaryType),
                NullIfEmpty(creditName) ?? fallbackArtistName ?? "Desconocido",
                rg.ArtistCredit.FirstOrDefault()?.Artist?.Id ?? fallbackArtistMbid ?? string.Empty,
                rg.Score,
                // "count" no esta garantizado por la API; la lista de releases que
                // viene en la misma respuesta sirve de respaldo.
                rg.ReleaseCount ?? rg.Releases.Count));
        }

        return items;
    }

    /// <summary>
    /// Mapea grabaciones a DTOs, eligiendo para cada una el album al que conviene
    /// llevar al usuario, y resolviendo en UNA sola query las portadas ya cacheadas.
    /// </summary>
    private async Task<List<SongSearchItemDto>> MapRecordingsAsync(
        IReadOnlyList<MbRecording> recordings,
        CancellationToken ct)
    {
        var releaseGroupIds = recordings
            .Select(recording => PickRelease(recording)?.ReleaseGroup?.Id)
            .OfType<string>()
            .Distinct()
            .ToArray();

        Dictionary<string, string> localCovers = [];

        if (releaseGroupIds.Length > 0)
        {
            localCovers = await _context.Albums
                .Where(a => releaseGroupIds.Contains(a.MusicBrainzId) && a.CoverArtUrl != null)
                .Select(a => new { a.MusicBrainzId, a.CoverArtUrl })
                .ToDictionaryAsync(a => a.MusicBrainzId, a => a.CoverArtUrl!, ct);
        }

        var items = new List<SongSearchItemDto>(recordings.Count);

        foreach (var recording in recordings)
        {
            var release = PickRelease(recording);
            var releaseGroupId = release?.ReleaseGroup?.Id;

            // La fecha de la grabacion si viene; si no, la de la edicion elegida.
            var (releaseDate, _) = PartialDate.Parse(
                recording.FirstReleaseDate ?? release?.Date);

            items.Add(new SongSearchItemDto(
                recording.Id,
                recording.Title,
                NullIfEmpty(recording.ArtistCredit.FormatCredit()) ?? "Desconocido",
                recording.ArtistCredit.FirstOrDefault()?.Artist?.Id ?? string.Empty,
                recording.Length,
                NullIfEmpty(release?.ReleaseGroup?.Title) ?? NullIfEmpty(release?.Title),
                releaseGroupId,
                releaseGroupId is null
                    ? null
                    : localCovers.GetValueOrDefault(releaseGroupId) ?? CoverUrlFor(releaseGroupId),
                releaseDate,
                VersionCount: 1,
                MatchScore: recording.Score));
        }

        return items;
    }

    /// <summary>
    /// Elige la edicion que mejor representa a la grabacion.
    /// </summary>
    /// <remarks>
    /// Una grabacion popular aparece en el album original, en tres recopilatorios y en
    /// dos bandas sonoras. Tomar la primera de la lista lleva al usuario a un
    /// "Greatest Hits" cuando lo que queria era el disco. Por eso se prefiere un
    /// release-group de tipo Album sin tipos secundarios —Compilation, Live y
    /// Soundtrack son secundarios— y, entre esos, el mas viejo, que es el lanzamiento
    /// original.
    /// </remarks>
    private static MbRelease? PickRelease(MbRecording recording) =>
        recording.Releases
            .Where(release => release.ReleaseGroup is not null)
            .OrderByDescending(release =>
                string.Equals(release.ReleaseGroup!.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase))
            .ThenBy(release => release.ReleaseGroup!.SecondaryTypes.Count)
            .ThenBy(release => PartialDate.Parse(release.Date).Date ?? DateOnly.MaxValue)
            .ThenBy(release => release.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// URL de portada para un listado. No comprueba que exista: ver <see cref="CoverArt"/>.
    /// </summary>
    private string CoverUrlFor(string releaseGroupMbid)
    {
        var options = _options.CurrentValue;

        return CoverArt.FrontUrl(options.CoverArtBaseUrl, releaseGroupMbid, options.CoverArtSize);
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
