using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.News;
using MusicReviews.Application.News.Dtos;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Noticias de musica, agregadas de feeds publicos.
/// </summary>
/// <remarks>
/// Solo titulo, extracto corto, imagen y enlace a la fuente original. Nada mas, y es
/// deliberado: republicar el articulo entero seria reproducir obra ajena y quitarle la
/// visita a quien la escribio.
/// </remarks>
[ApiController]
[Route("api/news")]
[AllowAnonymous]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class NewsController : ControllerBase
{
    private readonly INewsService _news;

    public NewsController(INewsService news)
    {
        _news = news;
    }

    /// <summary>Ultimas noticias. Lista vacia si ninguna fuente responde.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NewsItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatest(
        [FromQuery] int limit = NewsDefaults.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var result = await _news.GetLatestAsync(limit, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
