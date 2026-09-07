using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Search.Dtos;

namespace MusicReviews.Application.Search;

/// <summary>
/// Busqueda instantanea sobre el catalogo local, para el desplegable del buscador.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no consulta MusicBrainz.</b> Un desplegable que responde desde la primera
/// letra tiene que contestar en milisegundos, y MusicBrainz impone <b>1 request por
/// segundo para toda la aplicacion</b>. Escribir "nirvana" son siete pulsaciones: incluso
/// con debounce, cada consulta tardaria segundos y una sola persona escribiendo dejaria
/// sin catalogo a las demas. Spotify puede hacerlo porque el indice es suyo.
/// </para>
/// <para>
/// La salida es la misma: <b>el indice es nuestro</b>. Esto consulta unicamente Postgres
/// —artistas y albumes ya cacheados— y responde sin salir a ningun lado. La busqueda
/// completa contra MusicBrainz sigue existiendo detras de Enter, y ademas <b>siembra</b>
/// el catalogo con sus resultados, con lo que el indice local crece con el uso.
/// </para>
/// <para>
/// <b>Lo que esto no cubre.</b> Las canciones no estan en el indice porque la aplicacion
/// no las persiste: se resenia el album, no el tema. Aparecen en la pagina de resultados
/// completa, que si va a MusicBrainz.
/// </para>
/// </remarks>
public interface IQuickSearchService
{
    Task<Result<IReadOnlyList<QuickSearchItemDto>>> SearchAsync(
        string query,
        int limit = QuickSearchDefaults.DefaultLimit,
        CancellationToken cancellationToken = default);
}

public static class QuickSearchDefaults
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 20;

    /// <summary>
    /// Con una sola letra el desplegable ya responde, que es lo que se pidio. El minimo
    /// existe solo para no disparar la consulta con el campo vacio.
    /// </summary>
    public const int MinQueryLength = 1;
}
