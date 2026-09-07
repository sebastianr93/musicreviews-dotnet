using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.News;
using MusicReviews.Application.News.Dtos;

namespace MusicReviews.Infrastructure.News;

/// <inheritdoc cref="INewsService"/>
/// <remarks>
/// <para>
/// <b>Nada de esto se guarda en la base.</b> Una noticia no tiene estado propio en esta
/// aplicacion —no se puntua, no se comenta, no se guarda— asi que persistirla seria
/// mantener una copia de contenido ajeno que ademas hay que sincronizar cuando el medio
/// lo edita o lo baja. Alcanza con una cache en memoria.
/// </para>
/// <para>
/// <b>Se sirve viejo antes que vacio.</b> La cache guarda la ultima copia buena por
/// bastante mas tiempo del que la considera fresca. Cuando vence la frescura se intenta
/// refrescar, y si el medio no responde se devuelve igual lo que ya se tenia. Para una
/// seccion de relleno, una noticia de ayer es mejor que un hueco.
/// </para>
/// </remarks>
internal sealed class NewsService : INewsService
{
    private const string CacheKey = "news:latest";

    /// <summary>
    /// Serializa el refresco entre requests concurrentes.
    /// </summary>
    /// <remarks>
    /// Sin esto, veinte visitas simultaneas al home con la cache vencida disparan veinte
    /// rondas de descargas contra los mismos medios. El que entra refresca; los demas
    /// esperan y se encuentran el resultado ya en la cache. Es estatico porque el
    /// servicio es scoped: una instancia por request no podria coordinar nada.
    /// </remarks>
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<NewsOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NewsService> _logger;

    public NewsService(
        HttpClient http,
        IMemoryCache cache,
        IOptionsMonitor<NewsOptions> options,
        TimeProvider timeProvider,
        ILogger<NewsService> logger)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Copia cacheada, con el momento en que se armo.</summary>
    private sealed record Snapshot(IReadOnlyList<NewsItemDto> Items, DateTimeOffset FetchedAt);

    public async Task<Result<IReadOnlyList<NewsItemDto>>> GetLatestAsync(
        int limit = NewsDefaults.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var take = Math.Clamp(limit, 1, NewsDefaults.MaxLimit);

        if (options.Feeds.Count == 0)
        {
            return Result.Success<IReadOnlyList<NewsItemDto>>([]);
        }

        var snapshot = await GetSnapshotAsync(options, cancellationToken);

        return Result.Success<IReadOnlyList<NewsItemDto>>(
            [.. snapshot?.Items.Take(take) ?? []]);
    }

    private async Task<Snapshot?> GetSnapshotAsync(NewsOptions options, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var freshFor = TimeSpan.FromMinutes(options.RefreshMinutes);

        if (_cache.TryGetValue<Snapshot>(CacheKey, out var cached)
            && cached is not null
            && now - cached.FetchedAt < freshFor)
        {
            return cached;
        }

        await RefreshLock.WaitAsync(cancellationToken);

        try
        {
            // Otra request pudo haber refrescado mientras se esperaba el turno.
            if (_cache.TryGetValue<Snapshot>(CacheKey, out var afterWait)
                && afterWait is not null
                && now - afterWait.FetchedAt < freshFor)
            {
                return afterWait;
            }

            var items = await FetchAllAsync(options, cancellationToken);

            if (items.Count == 0)
            {
                // Ninguna fuente respondio. Si habia algo guardado se sigue mostrando:
                // una noticia de ayer es mejor que un hueco.
                _logger.LogWarning("Ninguna fuente de noticias respondio; se conserva la copia anterior.");

                return afterWait ?? cached;
            }

            var snapshot = new Snapshot(items, now);

            _cache.Set(CacheKey, snapshot, TimeSpan.FromHours(options.StaleHours));

            return snapshot;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    /// <summary>
    /// Descarga y parsea todas las fuentes, y las mezcla en un solo listado.
    /// </summary>
    /// <remarks>
    /// Van en paralelo: son sitios distintos, no hay ninguna cola compartida que
    /// respetar —al reves que con MusicBrainz— y esperarlos en fila multiplicaria el
    /// tiempo del refresco por la cantidad de feeds.
    /// </remarks>
    private async Task<List<NewsItemDto>> FetchAllAsync(
        NewsOptions options,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(
            options.Feeds.Select(feed => FetchOneAsync(feed, options, cancellationToken)));

        // Deduplicado por URL: los medios se republican entre si y la misma nota puede
        // venir de dos fuentes.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<NewsItemDto>();

        foreach (var item in results.SelectMany(items => items))
        {
            if (seen.Add(item.Url))
            {
                merged.Add(item);
            }
        }

        // Las que no traen fecha van al final: sin fecha no hay forma de saber si son de
        // hoy o de hace tres años, y mezclarlas arriba ensuciaria la seccion.
        return
        [
            .. merged
                .OrderByDescending(item => item.PublishedAt.HasValue)
                .ThenByDescending(item => item.PublishedAt ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
        ];
    }

    private async Task<IReadOnlyList<NewsItemDto>> FetchOneAsync(
        NewsFeedOptions feed,
        NewsOptions options,
        CancellationToken cancellationToken)
    {
        // Timeout propio por feed. El HttpClient no lleva uno global porque el mismo
        // cliente atiende a todas las fuentes y el presupuesto es de cada una.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var response = await _http.GetAsync(feed.Url, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "El feed {Feed} devolvio {StatusCode}", feed.Name, (int)response.StatusCode);

                return [];
            }

            var xml = await response.Content.ReadAsStringAsync(timeout.Token);

            return RssParser.Parse(xml, feed.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // El usuario cerro la pestaña. No es un problema del feed.
            throw;
        }
        catch (Exception ex)
        {
            // Un medio caido, un DNS que no resuelve, un certificado vencido: ninguna de
            // esas es una falla de esta aplicacion, y ninguna puede impedir que las
            // otras fuentes se muestren.
            _logger.LogWarning(ex, "No se pudo leer el feed {Feed} ({Url})", feed.Name, feed.Url);

            return [];
        }
    }
}
