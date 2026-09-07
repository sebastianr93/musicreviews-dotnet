using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Notifications;
using MusicReviews.Application.Notifications.Dtos;

namespace MusicReviews.Api.Controllers;

/// <summary>
/// Avisos del usuario autenticado. Todo el controlador es privado por definicion: no hay
/// ruta que permita mirar los avisos de otro, ni siquiera para un administrador.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    /// <summary>Listado paginado por cursor, del mas reciente al mas viejo.</summary>
    /// <param name="unreadOnly">Solo los que faltan leer.</param>
    [HttpGet]
    [ProducesResponseType<CursorPage<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = CursorRequest.DefaultLimit,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _notifications.GetAsync(
            new CursorRequest(cursor, limit), unreadOnly, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>
    /// Cantidad de avisos sin leer. Es el endpoint que consulta la campana, asi que se
    /// resuelve con un COUNT sobre el indice parcial y no trae ninguna fila.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var result = await _notifications.GetUnreadCountAsync(cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error);
    }

    /// <summary>Marca un aviso como leido. Idempotente.</summary>
    [HttpPost("{id:int}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(int id, CancellationToken cancellationToken)
    {
        var result = await _notifications.MarkAsReadAsync(id, cancellationToken);

        return result.IsSuccess ? NoContent() : this.Problem(result.Error);
    }

    /// <summary>Marca todos como leidos. Devuelve cuantos cambiaron.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType<MarkAllReadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var result = await _notifications.MarkAllAsReadAsync(cancellationToken);

        return result.IsSuccess
            ? Ok(new MarkAllReadResponse(result.Value))
            : this.Problem(result.Error);
    }
}

/// <summary>Cuantos avisos paso a leidos la operacion.</summary>
public sealed record MarkAllReadResponse(int MarkedAsRead);
