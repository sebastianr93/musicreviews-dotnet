using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Comments;
using MusicReviews.Application.Comments.Dtos;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Results;
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommentService> _logger;

    public CommentService(
        AppDbContext context,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<CommentService> logger)
    {
        _context = context;
        _currentUser = currentUser;
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
                Error.NotFound("reviews.not_found", "No existe esa review."));
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

        if (!await _context.Reviews.AnyAsync(r => r.Id == reviewId, cancellationToken))
        {
            return Result.Failure<CommentNodeDto>(
                Error.NotFound("reviews.not_found", "No existe esa review."));
        }

        var depth = 0;

        if (request.ParentCommentId is { } parentId)
        {
            var parent = await _context.Comments
                .AsNoTracking()
                .Where(c => c.Id == parentId)
                .Select(c => new { c.Id, c.ReviewId, c.Depth, c.IsDeleted })
                .FirstOrDefaultAsync(cancellationToken);

            if (parent is null)
            {
                return Result.Failure<CommentNodeDto>(Error.NotFound(
                    "comments.parent_not_found",
                    "El comentario al que estas respondiendo no existe."));
            }

            // Sin esta validacion se podria colgar una respuesta de un comentario de
            // otra review: el hilo se traeria por ReviewId y el nodo quedaria huerfano.
            if (parent.ReviewId != reviewId)
            {
                return Result.Failure<CommentNodeDto>(Error.Validation(
                    "comments.parent_other_review",
                    "El comentario al que respondes pertenece a otra review."));
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
                    $"El hilo alcanzo la profundidad maxima de {Comment.MaxDepth} niveles. " +
                    "Responde a un comentario mas arriba."));
            }

            depth = parent.Depth + 1;
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
        Error.Unauthorized("auth.required", "Tenes que iniciar sesion.");
}
