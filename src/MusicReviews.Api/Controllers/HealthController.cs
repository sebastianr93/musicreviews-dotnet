using Microsoft.AspNetCore.Mvc;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Endpoint de diagnostico: confirma que la Api levanta y que la conexion a Postgres funciona.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _context;

    public HealthController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var canConnect = await _context.Database.CanConnectAsync(cancellationToken);

        var payload = new
        {
            status = canConnect ? "healthy" : "degraded",
            database = canConnect ? "up" : "down",
            timestamp = DateTimeOffset.UtcNow
        };

        return canConnect
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }
}
