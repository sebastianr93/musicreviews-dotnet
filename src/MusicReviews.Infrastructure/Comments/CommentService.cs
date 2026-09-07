using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Comments;
using MusicReviews.Application.Comments.Dtos;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Notifications;
using MusicReviews.Application.Reviews.Dtos;
using MusicReviews.Domain.Constants;
using MusicReviews.Domain.Entities;
using MusicReviews.Domain.Enums;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Comments;

/// <inheritdoc cref="ICommentService"/>
internal sealed class CommentService : ICommentService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationWriter _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommentService> _logger;

    public CommentService(
        AppDbContext context,
        ICurrentUser currentUser,
        INotificationWriter notifications,
        TimeProvider timeProvider,
        ILogger<CommentService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Lectura del hilo
    // ------------------------------------------------------------------

    /// <remarks>
    /// Aca esta el punto del ejercicio: una sola consulta plana filtrada por
    /// <c>ReviewId</c>, y el arbol armado en memoria. La cantidad de consultas no
    /// depende de la profundidad ni de la forma del hilo.
    /// </remarks>
    public async Task<Result<IReadOnlyList<CommentNodeDto>>> GetTreeAsync(
        int reviewId,
        CommentSortOrder sort = CommentSortOrder.Oldest,
        CancellationToken cancellationToken = default)
    {
        var reviewExists = await _context.Reviews
            .AnyAsync(r => r.Id == reviewId, cancellationToken);

        if (!reviewExists)
        {
            return Result.Failure<IReadOnlyList<CommentNodeDto>>(
                Error.NotFound("reviews.not_found", "No existe esa reseña."));
        }

        // UNA consulta: todo el hilo, a cualquier profundidad, ordenado
        // cronologicamente para que las respuestas queden en orden al armar el arbol.
        // La sostiene el indice (ReviewId, CreatedAt).
        var flat = await _context.Comments
            .AsNoTracking()
            .Where(c => c.ReviewId == reviewId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(ToDto(_currentUser.UserId))
            .ToListAsync(cancellationToken);

        var roots = CommentTreeBuilder.Build(flat);

        return Result.Success(SortRoots(roots, sort));
    }

    public async Task<Result<CommentNodeDto>> GetByIdAsync(
        int commentId,
        CancellationToken cancellationToken = default)
    {
        var comment = await _context.Comments
            .AsNoTracking()
            .Where(c => c.Id == commentId)
            .Select(ToDto(_currentUser.UserId))
            .FirstOrDefaultAsync(cancellationToken);

        return comment is null
            ? Result.Failure<CommentNodeDto>(NotFound())
            : Result.Success(comment);
    }

    // ------------------------------------------------------------------
    // Escrituras
    // ------------------------------------------------------------------

    public async Task<Result<CommentNodeDto>> CreateAsync(
        int reviewId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<CommentNodeDto>(Unauthenticated());
        }

        var review = await _context.Reviews
            .AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => new { r.Id, r.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (review is null)
        {
            return Result.Failure<CommentNodeDto>(
                Error.NotFound("reviews.not_found", "No existe esa reseña."));
        }

        var depth = 0;
        Guid? parentAuthorId = null;

        if (request.ParentCommentId is { } parentId)
        {
            var parent = await _context.Comments
                .AsNoTracking()
                .Where(c => c.Id == parentId)
                .Select(c => new { c.Id, c.ReviewId, c.Depth, c.IsDeleted, c.UserId })
                .FirstOrDefaultAsync(cancellationToken);

            if (parent is null)
            {
                return Result.Failure<CommentNodeDto>(Error.NotFound(
                    "comments.parent_not_found",
                    "El comentario al que estás respondiendo no existe."));
            }

            // Sin esta validacion se podria colgar una respuesta de un comentario de
            // otra review: el hilo se traeria por ReviewId y el nodo quedaria huerfano.
            if (parent.ReviewId != reviewId)
            {
                return Result.Failure<CommentNodeDto>(Error.Validation(
                    "comments.parent_other_review",
                    "El comentario al que respondés pertenece a otra reseña."));
            }

            if (parent.IsDeleted)
            {
                return Result.Failure<CommentNodeDto>(Error.Validation(
                    "comments.parent_deleted",
                    "No se puede responder a un comentario borrado."));
            }

            // Depth guardado: el limite se aplica en O(1), sin subir por la cadena de padres.
            if (parent.Depth + 1 >= Comment.MaxDepth)
            {
                return Result.Failure<CommentNodeDto>(Error.Validation(
                    "comments.max_depth",
                    $"El hilo alcanzó la profundidad máxima de {Comment.MaxDepth} niveles. " +
                    "Respondé a un comentario más arriba."));
            }

            depth = parent.Depth + 1;
            parentAuthorId = parent.UserId;
        }

        var comment = new Comment
        {
            ReviewId = reviewId,
            UserId = userId,
            ParentCommentId = request.ParentCommentId,
            Depth = depth,
            Text = request.Text.Trim(),
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Comentario {CommentId} creado por {UserId} en la review {ReviewId} (profundidad {Depth})",
            comment.Id, userId, reviewId, depth);

        // El aviso va en una segunda escritura porque necesita el Id que acaba de
        // generar la base. Si fallara, el comentario ya quedo publicado y solo se pierde
        // el aviso: es la degradacion correcta, y por eso el error se registra sin
        // propagarse.
        await NotifyNewCommentAsync(comment, userId, parentAuthorId, review.UserId, cancellationToken);

        return await GetByIdAsync(comment.Id, cancellationToken);
    }

    public async Task<Result<CommentNodeDto>> UpdateAsync(
        int commentId,
        UpdateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<CommentNodeDto>(Unauthenticated());
        }

        var comment = await _context.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);

        if (comment is null)
        {
            return Result.Failure<CommentNodeDto>(NotFound());
        }

        if (comment.IsDeleted)
        {
            return Result.Failure<CommentNodeDto>(Error.Validation(
                "comments.deleted",
                "No se puede editar un comentario borrado."));
        }

        if (comment.UserId != userId)
        {
            return Result.Failure<CommentNodeDto>(Error.Forbidden(
                "comments.not_owner",
                "Solo el autor puede editar su comentario."));
        }

        comment.Text = request.Text.Trim();
        comment.UpdatedAt = _timeProvider.GetUtcNow();

        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(commentId, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int commentId, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure(Unauthenticated());
        }

        var comment = await _context.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken);

        if (comment is null)
        {
            return Result.Failure(NotFound());
        }

        var isAdmin = _currentUser.IsInRole(AppRoles.Admin);

        if (comment.UserId != userId && !isAdmin)
        {
            return Result.Failure(Error.Forbidden(
                "comments.not_owner",
                "Solo el autor o un administrador pueden borrar este comentario."));
        }

        if (comment.IsDeleted)
        {
            // Idempotente.
            return Result.Success();
        }

        // Borrado logico: el nodo queda en el arbol sosteniendo las respuestas de
        // terceros. El texto se conserva en la base para auditoria de moderacion,
        // pero la proyeccion nunca lo devuelve cuando IsDeleted es true.
        comment.IsDeleted = true;
        comment.DeletedAt = _timeProvider.GetUtcNow();

        // Los likes del comentario si se borran: no hay FK que los arrastre y
        // votar algo que ya no se muestra no tiene sentido.
        await _context.Likes
            .Where(l => l.TargetType == LikeTargetType.Comment && l.TargetId == commentId)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Comentario {CommentId} borrado (logico) por {UserId} (admin: {IsAdmin})",
            commentId, userId, isAdmin);

        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Avisos
    // ------------------------------------------------------------------

    /// <summary>
    /// Un comentario genera un solo aviso.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Si es una respuesta, el aviso va al autor del comentario padre; si es de primer
    /// nivel, al autor de la reseña. No se avisa a los dos: en un hilo largo el autor
    /// de la reseña recibiria una notificacion por cada respuesta anidada entre
    /// terceros, que es exactamente el ruido que hace que la gente deje de mirar la
    /// campana. Quien quiera seguir el hilo entero lo abre.
    /// </para>
    /// <para>
    /// El fallo del aviso no invalida el comentario, que ya esta guardado. Se registra
    /// y se sigue.
    /// </para>
    /// </remarks>
    private async Task NotifyNewCommentAsync(
        Comment comment,
        Guid actorId,
        Guid? parentAuthorId,
        Guid reviewAuthorId,
        CancellationToken cancellationToken)
    {
        var (recipientId, type) = parentAuthorId is { } parentAuthor
            ? (parentAuthor, NotificationType.CommentReplied)
            : (reviewAuthorId, NotificationType.ReviewCommented);

        try
        {
            await _notifications.NotifyAsync(
                recipientId,
                actorId,
                type,
                reviewId: comment.ReviewId,
                commentId: comment.Id,
                cancellationToken: cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "No se pudo generar el aviso del comentario {CommentId}. El comentario quedo publicado.",
                comment.Id);

            // El contexto puede haber quedado con la notificacion pendiente; se descarta
            // para que no vuelva a intentarse en un guardado posterior de esta request.
            _context.ChangeTracker.Clear();
        }
    }

    // ------------------------------------------------------------------
    // Proyeccion y orden
    // ------------------------------------------------------------------

    /// <summary>
    /// Proyeccion a DTO con los conteos de votos resueltos como subconsultas
    /// correlacionadas dentro de la misma sentencia. Los comentarios borrados no
    /// exponen ni texto ni autor.
    /// </summary>
    private Expression<Func<Comment, CommentNodeDto>> ToDto(Guid? currentUserId)
    {
        var likes = _context.Likes;

        return comment => new CommentNodeDto
        {
            Id = comment.Id,
            ParentCommentId = comment.ParentCommentId,
            Depth = comment.Depth,
            Text = comment.IsDeleted ? null : comment.Text,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt,
            IsDeleted = comment.IsDeleted,
            Author = comment.IsDeleted
                ? null
                : new AuthorDto(comment.User.Id, comment.User.UserName!, comment.User.AvatarUrl),
            LikeCount = likes.Count(l => l.TargetType == LikeTargetType.Comment
                                      && l.TargetId == comment.Id
                                      && l.IsLike),
            DislikeCount = likes.Count(l => l.TargetType == LikeTargetType.Comment
                                         && l.TargetId == comment.Id
                                         && !l.IsLike),
            CurrentUserVote = currentUserId == null
                ? null
                : likes
                    .Where(l => l.TargetType == LikeTargetType.Comment
                             && l.TargetId == comment.Id
                             && l.UserId == currentUserId)
                    .Select(l => (bool?)l.IsLike)
                    .FirstOrDefault()
        };
    }

    /// <summary>
    /// Ordena solo los nodos de primer nivel. Las respuestas quedan siempre en orden
    /// cronologico, que es como se lee una conversacion.
    /// </summary>
    private static IReadOnlyList<CommentNodeDto> SortRoots(
        IReadOnlyList<CommentNodeDto> roots,
        CommentSortOrder sort) => sort switch
        {
            CommentSortOrder.Newest =>
                [.. roots.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)],

            CommentSortOrder.MostLiked =>
                [.. roots.OrderByDescending(c => c.LikeCount - c.DislikeCount).ThenBy(c => c.CreatedAt)],

            // Oldest: ya vienen asi de la consulta.
            _ => roots
        };

    private static Error NotFound() =>
        Error.NotFound("comments.not_found", "No existe ese comentario.");

    private static Error Unauthenticated() =>
        Error.Unauthorized("auth.required", "Tenés que iniciar sesión.");
}
