using System.ComponentModel.DataAnnotations;

namespace MusicReviews.Infrastructure.News;

/// <summary>Configuracion del agregador de noticias.</summary>
public sealed class NewsOptions
{
    public const string SectionName = "News";

    /// <summary>
    /// Feeds a consultar. Se dejan configurables y no fijos en el codigo porque una URL
    /// de feed es lo primero que un medio cambia cuando migra de CMS, y eso no deberia
    /// requerir recompilar nada.
    /// </summary>
    public List<NewsFeedOptions> Feeds { get; set; } = [];

    /// <summary>
    /// Cada cuanto se vuelve a pedir un feed. Los medios publican unas pocas notas por
    /// dia: bajar de esto solo agrega trafico contra sitios ajenos sin traer nada nuevo.
    /// </summary>
    [Range(1, 1440)]
    public int RefreshMinutes { get; set; } = 30;

    /// <summary>
    /// Cuanto se conserva la ultima copia buena para poder servirla si la fuente deja de
    /// responder. Ver <c>NewsService</c>.
    /// </summary>
    [Range(1, 168)]
    public int StaleHours { get; set; } = 24;

    /// <summary>Timeout por feed. Corto: la portada no puede esperar a un medio lento.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>
    /// User-Agent con el que la app se identifica ante los medios. Varios rechazan al
    /// cliente HTTP por defecto de .NET, y algunos devuelven 403 sin ningun User-Agent.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string UserAgent { get; set; } = "MusicReviews/1.0 (+https://github.com/)";
}

public sealed class NewsFeedOptions
{
    /// <summary>Nombre del medio. Se muestra en la tarjeta: la atribucion no es opcional.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Name { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    public string Url { get; set; } = null!;
}
