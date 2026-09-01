using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Admin;
using MusicReviews.Application.Admin.Dtos;
using MusicReviews.Application.Common.Models;
using MusicReviews.Infrastructure;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Panel de administracion. Todo el controlador exige el rol Admin.
/// La moderacion de contenido no vive aca: los endpoints de borrado de reviews y
/// comentarios ya aceptan a un administrador como autor alternativo, asi que
/// duplicarlos solo agregaria dos caminos para la misma operacion.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.RequireAdmin)]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin)
    {
        _admin = admin;
    }

    /// <summary>Estadisticas del sitio: usuarios, actividad, contenido y albumes mas resenados.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<AdminStatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var result = await _admin.GetStatsAsync(cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Listado de usuarios con sus roles y actividad. <c>search</c> filtra por usuario o email.</summary>
    [HttpGet("users")]
    [ProducesResponseType<PagedResult<AdminUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _admin.GetUsersAsync(new PageRequest(page, pageSize), search, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Asigna un rol a un usuario. Idempotente.</summary>
    [HttpPost("users/{userId:guid}/roles")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        Guid userId,
        AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _admin.AssignRoleAsync(userId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Quita un rol. No permite que un administrador se degrade a si mismo ni que
    /// se quede el sistema sin ningun administrador.
    /// </summary>
    [HttpDelete("users/{userId:guid}/roles/{role}")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(
        Guid userId,
        string role,
        CancellationToken cancellationToken)
    {
        var result = await _admin.RemoveRoleAsync(userId, role, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }
}
