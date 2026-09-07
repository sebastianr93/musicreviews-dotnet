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

    /// <summary>
    /// Timeout de CADA intento HTTP, no del request completo. El HttpClient de
    /// MusicBrainz se configura sin timeout global a proposito: la espera en la cola
    /// de 1 req/seg ocurre dentro de su pipeline y consumiria ese presupuesto sin
    /// haber hecho todavia ninguna llamada. Ver MusicBrainzThrottlingHandler.
    /// </summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Dias que una entidad cacheada se considera vigente. Pasado ese plazo,
    /// la proxima consulta de detalle la refresca contra MusicBrainz.
    /// </summary>
    [Range(1, 3650)]
    public int CacheDays { get; set; } = 30;

    /// <summary>Minutos que se guardan en memoria los resultados de busqueda.</summary>
    [Range(1, 1440)]
    public int SearchCacheMinutes { get; set; } = 10;

    /// <summary>
    /// Cuantos resultados se le piden a MusicBrainz por busqueda, antes de reordenar
    /// y paginar del lado nuestro.
    /// </summary>
    /// <remarks>
    /// Se piden bastantes mas de los que se muestran a proposito. El orden que devuelve
    /// MusicBrainz es por relevancia textual, y con titulos homonimos —"The Dark Side of
    /// the Moon" tiene decenas— el disco que el usuario busca puede caer en la posicion
    /// 45. Reordenar solo la primera pagina no lo traeria nunca. Ademas paginar sale
    /// gratis: las paginas siguientes se sirven de la misma respuesta cacheada, sin
    /// consumir otro turno de la cola de 1 req/seg. El maximo que acepta la API es 100.
    /// </remarks>
    [Range(20, 100)]
    public int SearchFetchLimit { get; set; } = 100;

    /// <summary>Tamaño de la portada que se pide a Cover Art Archive: 250, 500 o 1200.</summary>
    [Range(250, 1200)]
    public int CoverArtSize { get; set; } = 500;
}
