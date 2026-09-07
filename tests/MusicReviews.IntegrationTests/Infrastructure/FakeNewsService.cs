using MusicReviews.Application.Common.Results;
using MusicReviews.Application.News;
using MusicReviews.Application.News.Dtos;

namespace MusicReviews.IntegrationTests.Infrastructure;

/// <summary>
/// Reemplaza al agregador de noticias durante los tests.
/// </summary>
/// <remarks>
/// Los feeds reales son sitios de terceros: meterlos en la suite la volveria lenta y
/// roja cada vez que un medio tenga un mal dia, y ademas los resultados cambian cada
/// hora. Lo que se prueba con este fake es el endpoint —recorte de <c>limit</c>, forma
/// de la respuesta—; el parseo del XML lo cubren los tests unitarios de
/// <c>RssParser</c>, que es donde vive la logica que puede romperse.
/// </remarks>
internal sealed class FakeNewsService : INewsService
{
    public const string SourceName = "Fuente de prueba";

    public Task<Result<IReadOnlyList<NewsItemDto>>> GetLatestAsync(
        int limit = NewsDefaults.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, NewsDefaults.MaxLimit);

        var items = Enumerable.Range(1, 12)
            .Select(index => new NewsItemDto(
                $"Noticia {index}",
                $"Extracto de la noticia {index}.",
                $"https://noticias.test/nota-{index}",
                $"https://noticias.test/imagen-{index}.jpg",
                SourceName,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-index)))
            .Take(take)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<NewsItemDto>>(items));
    }
}
