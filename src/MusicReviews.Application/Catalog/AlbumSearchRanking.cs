using MusicReviews.Application.Catalog.Dtos;

namespace MusicReviews.Application.Catalog;

/// <summary>
/// Reordena los resultados de una busqueda de albumes para que el disco que la gente
/// buscaba quede primero.
/// </summary>
/// <remarks>
/// <para>
/// <b>El problema.</b> Buscar "the dark side of the moon" devuelve decenas de
/// release-groups con ese titulo exacto. Todos matchean igual de bien, asi que el
/// indice de MusicBrainz les asigna el mismo <c>score</c>, y entre empates el orden
/// es arbitrario: el de Pink Floyd puede quedar en la posicion 45 y el de una banda
/// de tres oyentes en la primera.
/// </para>
/// <para>
/// <b>Por que no lo arregla la API.</b> MusicBrainz es un catalogo, no un ranking:
/// no tiene ninguna nocion de popularidad y no la expone. Ordenar por relevancia
/// textual es todo lo que puede hacer, y con titulos identicos la relevancia textual
/// no distingue nada.
/// </para>
/// <para>
/// <b>La señal que si existe.</b> La cantidad de ediciones (<c>ReleaseCount</c>) es
/// un proxy de popularidad sorprendentemente bueno: un disco reeditado en vinilo, CD,
/// remasterizado y por pais acumula cientos de releases; una autoedicion tiene una.
/// No mide gusto, mide cuanta industria hubo alrededor, que para "cual de estos
/// discos homonimos buscaba el usuario" es justo lo que hace falta.
/// </para>
/// <para>
/// El <c>score</c> se compara <b>contra el mejor de la tanda</b>, no en tramos fijos:
/// todo lo que quede dentro de <see cref="ScoreTolerance"/> puntos del maximo se
/// considera igual de relevante y compite por popularidad. Agrupar en tramos fijos de
/// 10 no sirve —100 y 97 caerian en tramos distintos por estar a los lados de un limite
/// arbitrario— y esa diferencia es ruido del indice, no una señal que deba pesar mas
/// que 400 ediciones contra una.
/// </para>
/// </remarks>
public static class AlbumSearchRanking
{
    /// <summary>
    /// Cuantos puntos por debajo del mejor score se sigue considerando "igual de
    /// relevante". Ver la nota de la clase.
    /// </summary>
    public const int ScoreTolerance = 10;

    /// <summary>
    /// Prioridad por tipo primario: buscando un disco, un album vale mas que un
    /// single homonimo. Menor es mejor.
    /// </summary>
    private static int TypeRank(string? primaryType) => primaryType?.ToLowerInvariant() switch
    {
        "album" => 0,
        "ep" => 1,
        "single" => 2,
        null or "" => 4,
        _ => 3
    };

    public static IReadOnlyList<AlbumSummaryDto> Rank(IEnumerable<AlbumSummaryDto> albums)
    {
        var list = albums as IReadOnlyCollection<AlbumSummaryDto> ?? [.. albums];

        if (list.Count == 0)
        {
            return [];
        }

        var threshold = list.Max(album => album.MatchScore) - ScoreTolerance;

        return
        [
            .. list
                // Primero todo lo que matchea casi tan bien como el mejor resultado.
                .OrderByDescending(album => album.MatchScore >= threshold)
                // Fuera de ese grupo el score si manda; dentro queda neutralizado
                // para que decida la popularidad.
                .ThenByDescending(album => album.MatchScore >= threshold ? 0 : album.MatchScore)
                .ThenByDescending(album => album.ReleaseCount)
                .ThenBy(album => TypeRank(album.PrimaryType))
                // El original antes que los homonimos posteriores. Los que no tienen
                // fecha van al final: sin fecha no hay forma de saber si son relevantes.
                .ThenBy(album => album.ReleaseDate ?? DateOnly.MaxValue)
                // Desempate final para que el orden sea determinista entre llamadas.
                .ThenBy(album => album.MusicBrainzId, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Ordena artistas por relevancia. No hay proxy de popularidad para artistas en
    /// la API, pero el problema tampoco es tan grave: los nombres de artista son
    /// bastante mas discriminantes que los titulos de disco.
    /// </summary>
    /// <remarks>
    /// Nombre distinto y no una sobrecarga de <see cref="Rank"/>: con dos sobrecargas
    /// que solo difieren en el tipo del elemento, pasar una coleccion vacia no compila
    /// porque el compilador no puede inferir cual es.
    /// </remarks>
    public static IReadOnlyList<ArtistSearchItemDto> RankArtists(IEnumerable<ArtistSearchItemDto> artists) =>
        [.. artists
            .OrderByDescending(artist => artist.MatchScore)
            // Un artista ya cacheado localmente es uno que alguien de este sitio ya
            // consulto: es mejor apuesta que uno que nadie miro nunca.
            .ThenByDescending(artist => artist.Id.HasValue)
            .ThenBy(artist => artist.MusicBrainzId, StringComparer.Ordinal)];
}
