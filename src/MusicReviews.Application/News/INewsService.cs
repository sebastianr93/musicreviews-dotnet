using MusicReviews.Application.Common.Results;
using MusicReviews.Application.News.Dtos;

namespace MusicReviews.Application.News;

/// <summary>Noticias de musica, agregadas de feeds RSS/Atom publicos.</summary>
public interface INewsService
{
    /// <summary>
    /// Ultimas noticias, mezcladas de todas las fuentes y de la mas nueva a la mas vieja.
    /// </summary>
    /// <remarks>
    /// Devuelve una lista vacia —nunca un error— cuando ninguna fuente responde. Es
    /// relleno del feed: que un medio se caiga no puede romper la portada.
    /// </remarks>
    Task<Result<IReadOnlyList<NewsItemDto>>> GetLatestAsync(
        int limit = NewsDefaults.DefaultLimit,
        CancellationToken cancellationToken = default);
}

public static class NewsDefaults
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 30;
}
