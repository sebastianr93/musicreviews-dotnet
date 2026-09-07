using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Resuelve la foto de un artista.
/// </summary>
/// <remarks>
/// <para>
/// <b>MusicBrainz no tiene imagenes.</b> Es un catalogo de metadatos y no aloja fotos;
/// lo que si tiene son <i>relaciones</i> hacia otros sitios, y una de ellas apunta a
/// Wikidata. Wikidata guarda en la propiedad <c>P18</c> el nombre del archivo de imagen
/// en Wikimedia Commons, y Commons expone ese archivo por una URL construible. La cadena
/// completa es: MBID → relacion wikidata → P18 → Commons.
/// </para>
/// <para>
/// <b>El primer paso sale gratis.</b> La relacion a Wikidata viene en el mismo lookup del
/// artista agregando <c>inc=url-rels</c>, asi que no consume otro turno de la cola de 1
/// request por segundo de MusicBrainz. Solo la llamada a Wikidata es adicional, y
/// Wikidata no impone ese limite.
/// </para>
/// <para>
/// <b>Por que Commons y no otra fuente.</b> Las imagenes de Commons tienen licencia libre
/// y son enlazables sin permiso especial. Cualquier otra fuente —resultados de un buscador,
/// la foto de un perfil— seria republicar material ajeno sin derecho a hacerlo.
/// </para>
/// </remarks>
internal interface IWikidataClient
{
    /// <summary>
    /// Devuelve la URL de la imagen del elemento, o null si no tiene o si Wikidata no
    /// responde. La foto es opcional: nunca puede tumbar la consulta del artista.
    /// </summary>
    Task<string?> GetImageUrlAsync(string entityId, CancellationToken ct);
}

internal sealed class WikidataClient : IWikidataClient
{
    public const string HttpClientName = "wikidata";

    /// <summary>Propiedad "imagen" en Wikidata.</summary>
    private const string ImageProperty = "P18";

    /// <summary>
    /// Ancho al que Commons reescala la foto. Sin esto llega el original, que en muchos
    /// casos son varios megabytes de escaneo.
    /// </summary>
    private const int ImageWidth = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<WikidataClient> _logger;

    public WikidataClient(HttpClient http, ILogger<WikidataClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string?> GetImageUrlAsync(string entityId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var url = $"w/api.php?action=wbgetclaims&entity={Uri.EscapeDataString(entityId)}" +
                  $"&property={ImageProperty}&format=json";

        try
        {
            var claims = await _http.GetFromJsonAsync<WdClaimsResponse>(url, JsonOptions, ct);

            var fileName = claims?.Claims
                ?.GetValueOrDefault(ImageProperty)
                ?.Select(claim => claim.MainSnak?.DataValue?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return fileName is null ? null : CommonsFileUrl(fileName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "No se pudo resolver la imagen de {EntityId} en Wikidata", entityId);
            return null;
        }
    }

    /// <summary>
    /// URL de un archivo de Commons a partir de su nombre.
    /// </summary>
    /// <remarks>
    /// <c>Special:FilePath</c> resuelve el nombre a la ubicacion real del archivo, que
    /// depende de un hash del nombre y no se puede construir a mano. Ademas acepta
    /// <c>width</c>, con lo que Commons devuelve una miniatura ya reescalada.
    /// </remarks>
    private static string CommonsFileUrl(string fileName) =>
        "https://commons.wikimedia.org/wiki/Special:FilePath/" +
        $"{Uri.EscapeDataString(fileName)}?width={ImageWidth}";

    // Contratos de la respuesta de wbgetclaims. Solo se modela el camino hasta el
    // nombre del archivo; el resto de la estructura no interesa.

    private sealed record WdClaimsResponse
    {
        [JsonPropertyName("claims")]
        public Dictionary<string, List<WdClaim>>? Claims { get; init; }
    }

    private sealed record WdClaim
    {
        [JsonPropertyName("mainsnak")]
        public WdSnak? MainSnak { get; init; }
    }

    private sealed record WdSnak
    {
        [JsonPropertyName("datavalue")]
        public WdDataValue? DataValue { get; init; }
    }

    private sealed record WdDataValue
    {
        /// <summary>Para P18 es el nombre del archivo en Commons, como cadena.</summary>
        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }
}
