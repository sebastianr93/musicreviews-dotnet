namespace MusicReviews.Application.News.Dtos;

/// <summary>
/// Una noticia, reducida a lo minimo que se puede publicar de un contenido ajeno.
/// </summary>
/// <remarks>
/// <b>Titulo, extracto corto, imagen y enlace a la fuente original.</b> Nada mas, y es
/// deliberado: republicar el articulo entero seria reproducir obra ajena, y ademas
/// quitarle la visita a quien la escribio. Lo que se hace aca es lo que hace cualquier
/// agregador serio —mostrar lo justo para que alguien decida si le interesa y mandarlo
/// al sitio original—, y por eso <see cref="SourceName"/> viaja siempre: el lector tiene
/// que saber de donde salio antes de hacer click.
/// </remarks>
public sealed record NewsItemDto(
    string Title,
    string? Excerpt,
    string Url,
    string? ImageUrl,
    string SourceName,
    DateTimeOffset? PublishedAt);
