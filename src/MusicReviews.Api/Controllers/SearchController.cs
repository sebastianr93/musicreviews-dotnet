using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Search;
using MusicReviews.Application.Search.Dtos;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Busqueda instantanea para el desplegable del buscador.
/// </summary>
/// <remarks>
/// Vive aparte de <c>CatalogController</c> y bajo otra politica de rate limiting porque
/// es otra cosa: el catalogo sale a MusicBrainz y esta limitado a 30 requests por minuto
/// justamente por eso, mientras que esto es una consulta a Postgres que se dispara con
/// cada tecla. Meterlas en la misma politica dejaria al usuario sin catalogo por haber
/// escrito rapido.
/// </remarks>
[ApiController]
[Route("api/search")]
[AllowAnonymous]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Suggest)]
public class SearchController : ControllerBase
{
    private readonly IQuickSearchService _search;

    public SearchController(IQuickSearchService search)
    {
        _search = search;
    }

    /// <summary>
    /// Sugerencias del catalogo local, mezcladas y ordenadas por popularidad.
    /// Devuelve lista vacia si no hay nada: nunca un error.
    /// </summary>
    [HttpGet("quick")]
    [ProducesResponseType<IReadOnlyList<QuickSearchItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Quick(
        [FromQuery] string q = "",
        [FromQuery] int limit = QuickSearchDefaults.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var result = await _search.SearchAsync(q, limit, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
