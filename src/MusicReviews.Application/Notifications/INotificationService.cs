using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Notifications.Dtos;

namespace MusicReviews.Application.Notifications;

/// <summary>Lectura y gestion de los avisos del usuario autenticado.</summary>
public interface INotificationService
{
    Task<Result<CursorPage<NotificationDto>>> GetAsync(
        CursorRequest request,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default);

    Task<Result<UnreadCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Marca un aviso como leido. Idempotente. Solo el destinatario.</summary>
    Task<Result> MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Marca todos los avisos del usuario como leidos. Devuelve cuantos cambiaron.</summary>
    Task<Result<int>> MarkAllAsReadAsync(CancellationToken cancellationToken = default);
}
