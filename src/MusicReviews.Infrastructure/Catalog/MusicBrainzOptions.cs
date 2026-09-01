using System.ComponentModel.DataAnnotations;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Configuracion del cliente de MusicBrainz y Cover Art Archive.
/// </summary>
public sealed class MusicBrainzOptions
{
    public const string SectionName = "MusicBrainz";

    [Required]
    public string BaseUrl { get; set; } = "https://musicbrainz.org/ws/2/";

    [Required]
    public string CoverArtBaseUrl { get; set; } = "https://coverartarchive.org/";

    /// <summary>
    /// User-Agent con el que la app se identifica. MusicBrainz lo exige: sin un
    /// User-Agent que identifique la aplicacion y de una via de contacto, la API
    /// responde 403. Formato: "Nombre/version ( url-o-mail-de-contacto )".
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string UserAgent { get; set; } = null!;

    /// <summary>
    /// Intervalo minimo entre requests a MusicBrainz, en milisegundos.
    /// El limite publicado es 1 req/seg para clientes identificados; 1100 ms deja
    /// margen para el jitter de red y evita que el servidor cuente dos requests
    /// dentro del mismo segundo.
    /// </summary>
    [Range(0, 10_000)]
    public int MinIntervalMilliseconds { get; set; } = 1100;

    /// <summary>Reintentos ante 503 (la respuesta con la que MusicBrainz limita).</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 3;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Dias que una entidad cacheada se considera vigente. Pasado ese plazo,
    /// la proxima consulta de detalle la refresca contra MusicBrainz.
    /// </summary>
    [Range(1, 3650)]
    public int CacheDays { get; set; } = 30;

    /// <summary>Minutos que se guardan en memoria los resultados de busqueda.</summary>
    [Range(1, 1440)]
    public int SearchCacheMinutes { get; set; } = 10;

    /// <summary>Tamanio de la portada que se pide a Cover Art Archive: 250, 500 o 1200.</summary>
    [Range(250, 1200)]
    public int CoverArtSize { get; set; } = 500;
}
