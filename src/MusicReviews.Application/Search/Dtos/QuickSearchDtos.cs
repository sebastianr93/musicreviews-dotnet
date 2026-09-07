namespace MusicReviews.Application.Search.Dtos;

/// <summary>Que clase de cosa es un resultado del desplegable.</summary>
public enum QuickSearchKind
{
    Artist = 1,
    Album = 2
}

/// <summary>
/// Una fila del desplegable de busqueda. Los tres tipos comparten forma a proposito:
/// el desplegable los muestra mezclados en una sola lista ordenada, no separados por
/// secciones, asi que el cliente no tiene que discriminar el tipo para poder pintarlos.
/// </summary>
/// <param name="Subtitle">
/// La linea chica: el artista si es un album, el tipo y el pais si es un artista.
/// </param>
/// <param name="Popularity">
/// Popularidad local con la que se ordeno. Viaja en la respuesta para que se pueda ver
/// por que un resultado quedo arriba sin tener que leer el codigo del servicio.
/// </param>
public sealed record QuickSearchItemDto(
    QuickSearchKind Kind,
    string MusicBrainzId,
    string Title,
    string? Subtitle,
    string? ImageUrl,
    int? Year,
    int Popularity);
