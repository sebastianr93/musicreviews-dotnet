using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Resuelve la portada de un release-group en Cover Art Archive.
/// </summary>
/// <remarks>
/// La URL de portada es deterministica a partir del MBID, asi que no hace falta
/// consultar un indice: alcanza con construirla. Lo unico que no se sabe de antemano
/// es si esa portada existe, y por eso se hace un HEAD antes de cachearla. Se paga
/// una vez por album, cuando se lo trae por primera vez, y evita guardar URLs rotas
/// que despues el frontend tendria que descubrir con un 404 por cada render.
/// </remarks>
internal interface ICoverArtArchiveClient
{
    Task<string?> GetReleaseGroupCoverUrlAsync(string releaseGroupMbid, CancellationToken ct);
}

internal sealed class CoverArtArchiveClient : ICoverArtArchiveClient
{
    public const string HttpClientName = "coverartarchive";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<MusicBrainzOptions> _options;
    private readonly ILogger<CoverArtArchiveClient> _logger;

    public CoverArtArchiveClient(
        HttpClient http,
        IOptionsMonitor<MusicBrainzOptions> options,
        ILogger<CoverArtArchiveClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GetReleaseGroupCoverUrlAsync(string releaseGroupMbid, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var relativeUrl = $"release-group/{Uri.EscapeDataString(releaseGroupMbid)}/front-{options.CoverArtSize}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, relativeUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // CAA responde con un redirect a la imagen en archive.org; el HttpClient
            // lo sigue solo, asi que un 2xx aca significa que la portada existe.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cover Art Archive devolvio {StatusCode} para {Mbid}",
                    (int)response.StatusCode,
                    releaseGroupMbid);

                return null;
            }

            return new Uri(_http.BaseAddress!, relativeUrl).ToString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // La portada es opcional: que Cover Art Archive no responda no puede
            // tumbar la consulta del album.
            _logger.LogWarning(ex, "No se pudo resolver la portada de {Mbid}", releaseGroupMbid);
            return null;
        }
    }
}
