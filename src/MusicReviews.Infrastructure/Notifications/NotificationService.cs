using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Notifications;
using MusicReviews.Application.Notifications.Dtos;
using MusicReviews.Application.Reviews.Dtos;
using MusicReviews.Domain.Entities;
using MusicReviews.Domain.Enums;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Notifications;

/// <inheritdoc cref="INotificationService"/>
internal sealed class NotificationService : INotificationService, INotificationWriter
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext context,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Escritura (INotificationWriter)
    // ------------------------------------------------------------------

    public async Task NotifyAsync(
        Guid recipientId,
        Guid actorId,
        NotificationType type,
        int? reviewId = null,
        int? commentId = null,
        bool? isLike = null,
        CancellationToken cancellationToken = default)
    {
        // Nadie necesita que le avisen de lo que acaba de hacer: comentar la propia
        // reseña o votar el propio comentario no genera nada.
        if (recipientId == actorId)
        {
            return;
        }

        // Un aviso equivalente sin leer ya cumple su funcion. Sin esto, retirar y volver
        // a poner un voto acumula un aviso por cada vez.
        var existing = await _context.Notifications
            .FirstOrDefaultAsync(
                n => n.RecipientId == recipientId
                     && n.ActorId == actorId
                     && n.Type == type
                     && n.ReviewId == reviewId
                     && n.CommentId == commentId
                     && !n.IsRead,
                cancellationToken);

        var now = _timeProvider.GetUtcNow();

        if (existing is not null)
        {
            // Se refresca en vez de duplicar, para que vuelva a subir en el listado.
            existing.CreatedAt = now;
            existing.IsLike = isLike;
            return;
        }

        _context.Notifications.Add(new Notification
        {
            RecipientId = recipientId,
            ActorId = actorId,
            Type = type,
            ReviewId = reviewId,
            CommentId = commentId,
            IsLike = isLike,
            CreatedAt = now
        });
    }

    public async Task WithdrawVoteNotificationAsync(
        Guid actorId,
        NotificationType type,
        int? reviewId,
        int? commentId,
        CancellationToken cancellationToken = default)
    {
        var pending = await _context.Notifications
            .Where(n => n.ActorId == actorId
                        && n.Type == type
                        && n.ReviewId == reviewId
                        && n.CommentId == commentId
                        && !n.IsRead)
            .ToListAsync(cancellationToken);

        // Solo se retiran los que todavia no se leyeron: si el usuario ya lo vio,
        // borrarlo seria reescribir su historial.
        if (pending.Count > 0)
        {
            _context.Notifications.RemoveRange(pending);
        }
    }

    // ------------------------------------------------------------------
    // Lectura (INotificationService)
    // ------------------------------------------------------------------

    public async Task<Result<CursorPage<NotificationDto>>> GetAsync(
        CursorRequest request,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<CursorPage<NotificationDto>>(Unauthenticated());
        }

        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        // La condicion de keyset entera se traduce a SQL: el Id es numerico y de una
        // sola tabla, asi que no hace falta traer de mas ni descartar en memoria como
        // en el timeline de actividad.
        if (KeysetCursor.TryDecode(request.Cursor) is { } cursor)
        {
            query = query.Where(n =>
                n.CreatedAt < cursor.CreatedAt
                || (n.CreatedAt == cursor.CreatedAt && n.Id < cursor.Id));
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(request.Limit + 1)
            .Select(n => new NotificationDto(
                n.Id,
                n.Type,
                n.CreatedAt,
                n.IsRead,
                n.Actor == null
                    ? null
                    : new AuthorDto(n.Actor.Id, n.Actor.UserName!, n.Actor.AvatarUrl),
                n.ReviewId,
                n.CommentId,
                n.Review == null
                    ? null
                    : new NotificationAlbumDto(
                        n.Review.Album.MusicBrainzId,
                        n.Review.Album.Title,
                        n.Review.Album.CoverArtUrl,
                        n.Review.Album.Artist.Name),
                n.IsLike))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > request.Limit;
        var items = hasMore ? rows[..request.Limit] : rows;

        var next = hasMore && items.Count > 0
            ? new KeysetCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return Result.Success(new CursorPage<NotificationDto>(items, next));
    }

    public async Task<Result<UnreadCountDto>> GetUnreadCountAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<UnreadCountDto>(Unauthenticated());
        }

        // Resuelto por el indice parcial IX_Notifications_RecipientId_Unread.
        var unread = await _context.Notifications
            .CountAsync(n => n.RecipientId == userId && !n.IsRead, cancellationToken);

        return Result.Success(new UnreadCountDto(unread));
    }

    public async Task<Result> MarkAsReadAsync(
        int notificationId,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure(Unauthenticated());
        }

        // El filtro por destinatario va en el UPDATE, no en una consulta previa: asi
        // marcar el aviso de otro no es "prohibido", es imposible.
        var affected = await _context.Notifications
            .Where(n => n.Id == notificationId && n.RecipientId == userId && !n.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, _timeProvider.GetUtcNow()),
                cancellationToken);

        if (affected > 0)
        {
            return Result.Success();
        }

        // No cambio nada: o ya estaba leido (idempotente) o no existe / no es suyo.
        var exists = await _context.Notifications
            .AnyAsync(n => n.Id == notificationId && n.RecipientId == userId, cancellationToken);

        return exists
            ? Result.Success()
            : Result.Failure(Error.NotFound("notifications.not_found", "No existe esa notificación."));
    }

    public async Task<Result<int>> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<int>(Unauthenticated());
        }

        var affected = await _context.Notifications
            .Where(n => n.RecipientId == userId && !n.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, _timeProvider.GetUtcNow()),
                cancellationToken);

        _logger.LogInformation("{Count} avisos marcados como leidos por {UserId}", affected, userId);

        return Result.Success(affected);
    }

    private static Error Unauthenticated() =>
        Error.Unauthorized("auth.required", "Tenés que iniciar sesión.");
}
