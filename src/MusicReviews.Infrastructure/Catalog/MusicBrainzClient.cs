using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicReviews.Infrastructure.Catalog.Contracts;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Cliente HTTP de la API de MusicBrainz. Solo habla HTTP y deserializa:
/// no toca la base ni decide politicas de cache.
/// </summary>
internal interface IMusicBrainzClient
{
    Task<MbArtistSearchResponse?> SearchArtistsAsync(string query, int limit, int offset, CancellationToken ct);

    Task<MbArtist?> GetArtistAsync(string mbid, CancellationToken ct);

    Task<MbReleaseGroupSearchResponse?> SearchReleaseGroupsAsync(string query, int limit, int offset, CancellationToken ct);

    Task<MbReleaseGroupBrowseResponse?> BrowseArtistReleaseGroupsAsync(string artistMbid, int limit, int offset, CancellationToken ct);

    Task<MbReleaseGroup?> GetReleaseGroupAsync(string mbid, CancellationToken ct);
}

internal sealed class MusicBrainzClient : IMusicBrainzClient
{
    public const string HttpClientName = "musicbrainz";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<MusicBrainzClient> _logger;

    public MusicBrainzClient(HttpClient http, ILogger<MusicBrainzClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<MbArtistSearchResponse?> SearchArtistsAsync(
        string query, int limit, int offset, CancellationToken ct) =>
        GetAsync<MbArtistSearchResponse>(
            $"artist?query={EscapeLucene(query)}&limit={limit}&offset={offset}&fmt=json", ct);

    public Task<MbArtist?> GetArtistAsync(string mbid, CancellationToken ct) =>
        GetAsync<MbArtist>($"artist/{Uri.EscapeDataString(mbid)}?fmt=json", ct);

    public Task<MbReleaseGroupSearchResponse?> SearchReleaseGroupsAsync(
        string query, int limit, int offset, CancellationToken ct) =>
        GetAsync<MbReleaseGroupSearchResponse>(
            $"release-group?query={EscapeLucene(query)}&limit={limit}&offset={offset}&fmt=json", ct);

    public Task<MbReleaseGroupBrowseResponse?> BrowseArtistReleaseGroupsAsync(
        string artistMbid, int limit, int offset, CancellationToken ct) =>
        GetAsync<MbReleaseGroupBrowseResponse>(
            $"release-group?artist={Uri.EscapeDataString(artistMbid)}" +
            $"&type={Uri.EscapeDataString("album|ep")}" +
            $"&limit={limit}&offset={offset}&fmt=json", ct);

    public Task<MbReleaseGroup?> GetReleaseGroupAsync(string mbid, CancellationToken ct) =>
        GetAsync<MbReleaseGroup>(
            $"release-group/{Uri.EscapeDataString(mbid)}?inc=artist-credits&fmt=json", ct);

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken ct)
        where T : class
    {
        using var response = await _http.GetAsync(relativeUrl, ct);

        // Un MBID inexistente es un caso normal del flujo, no una falla de red.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // MusicBrainz responde 400 (no 404) cuando el MBID es sintacticamente valido
        // pero no corresponde a ninguna entidad, y tambien cuando la query de busqueda
        // esta mal formada. En los dos casos el resultado correcto hacia afuera es
        // "no hay nada", nunca un 500: el input del cliente no puede romper la Api.
        // Se loguea igual, porque un 400 en una busqueda significa que el escapado
        // de Lucene tiene un agujero y eso si es un bug nuestro.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning(
                "MusicBrainz devolvio 400 para {Url}. Se trata como 'sin resultados'.",
                relativeUrl);

            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "MusicBrainz devolvio {StatusCode} para {Url}",
                (int)response.StatusCode,
                relativeUrl);

            response.EnsureSuccessStatusCode();
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    /// <summary>
    /// Escapa los caracteres reservados de Lucene antes de mandar el texto del usuario
    /// como query. Sin esto, una busqueda con ':' o '"' rompe la sintaxis y MusicBrainz
    /// responde 400 en vez de "sin resultados".
    /// </summary>
    private static string EscapeLucene(string input)
    {
        const string reserved = @"+-&|!(){}[]^""~*?:\/";

        var builder = new StringBuilder(input.Length + 16);

        foreach (var c in input)
        {
            if (reserved.Contains(c))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return Uri.EscapeDataString(builder.ToString());
    }
}
