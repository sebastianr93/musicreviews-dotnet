using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Likes;
using MusicReviews.Application.Likes.Dtos;
using MusicReviews.Application.Notifications;
using MusicReviews.Domain.Entities;
using MusicReviews.Domain.Enums;
using MusicReviews.Infrastructure.Persistence;
using Npgsql;

namespace MusicReviews.Infrastructure.Likes;

/// <inheritdoc cref="ILikeService"/>
internal sealed class LikeService : ILikeService
{
    private const string UniqueViolationSqlState = "23505";

    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationWriter _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LikeService> _logger;

    public LikeService(
        AppDbContext context,
        ICurrentUser currentUser,
        INotificationWriter notifications,
        TimeProvider timeProvider,
        ILogger<LikeService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<VoteResultDto>> ToggleAsync(
        LikeTargetType targetType,
        int targetId,
        ToggleVoteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<VoteResultDto>(Unauthenticated());
        }

        var target = await ResolveTargetAsync(targetType, targetId, cancellationToken);

        if (target.Problem is { } targetError)
        {
            return Result.Failure<VoteResultDto>(targetError);
        }

        var existing = await _context.Likes
            .FirstOrDefaultAsync(
                l => l.UserId == userId && l.TargetType == targetType && l.TargetId == targetId,
                cancellationToken);

        if (existing is null)
        {
            _context.Likes.Add(new Like
            {
                UserId = userId,
                TargetType = targetType,
                TargetId = targetId,
                IsLike = request.IsLike,
                CreatedAt = _timeProvider.GetUtcNow()
            });

            // El aviso se encola antes del guardado para que viaje en la misma
            // transaccion que el voto: o quedan los dos, o no queda ninguno.
            await NotifyVoteAsync(targetType, targetId, target.OwnerId, userId, request.IsLike, cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Dos clicks simultaneos sobre el mismo boton. El indice unico
                // (UserId, TargetType, TargetId) impide el voto duplicado; el estado
                // que corresponde devolver es el que quedo en la base. El aviso tampoco
                // se pierde: lo escribio la request que llego primero.
                _logger.LogDebug(
                    "Voto concurrente de {UserId} sobre {TargetType} {TargetId}",
                    userId, targetType, targetId);

                _context.ChangeTracker.Clear();
            }
        }
        else if (existing.IsLike == request.IsLike)
        {
            // Mismo voto: es un toggle, se retira. El aviso se va con el.
            _context.Likes.Remove(existing);

            await WithdrawVoteAsync(targetType, targetId, userId, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Voto contrario: se cambia en lugar de crear otra fila. El aviso sin leer
            // se actualiza al sentido nuevo en vez de sumar uno mas.
            existing.IsLike = request.IsLike;
            existing.CreatedAt = _timeProvider.GetUtcNow();

            await NotifyVoteAsync(targetType, targetId, target.OwnerId, userId, request.IsLike, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await GetVoteStateAsync(targetType, targetId, userId, cancellationToken));
    }

    public async Task<Result<VoteResultDto>> RemoveAsync(
        LikeTargetType targetType,
        int targetId,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<VoteResultDto>(Unauthenticated());
        }

        var target = await ResolveTargetAsync(targetType, targetId, cancellationToken);

        if (target.Problem is { } targetError)
        {
            return Result.Failure<VoteResultDto>(targetError);
        }

        await _context.Likes
            .Where(l => l.UserId == userId && l.TargetType == targetType && l.TargetId == targetId)
            .ExecuteDeleteAsync(cancellationToken);

        await WithdrawVoteAsync(targetType, targetId, userId, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await GetVoteStateAsync(targetType, targetId, userId, cancellationToken));
    }

    /// <summary>
    /// Resultado de resolver el objetivo de un voto: el error si no se puede votar, o
    /// el autor del objetivo, que es a quien hay que avisarle.
    /// </summary>
    private readonly record struct VoteTarget(Error? Problem, Guid OwnerId)
    {
        public static VoteTarget Invalid(Error error) => new(error, Guid.Empty);

        public static VoteTarget Of(Guid ownerId) => new(null, ownerId);
    }

    /// <summary>
    /// Verifica que el target exista y devuelve su autor. La comprobacion es necesaria
    /// justamente porque la relacion es polimorfica: no hay FK que rechace un voto sobre
    /// un id inexistente, asi que sin esto la tabla acumularia votos apuntando a la nada.
    /// El autor se trae en la misma consulta porque el aviso lo necesita y una segunda
    /// consulta para pedir lo que ya estaba en la fila seria gratuita.
    /// </summary>
    private async Task<VoteTarget> ResolveTargetAsync(
        LikeTargetType targetType,
        int targetId,
        CancellationToken cancellationToken)
    {
        switch (targetType)
        {
            case LikeTargetType.Review:
                var review = await _context.Reviews
                    .AsNoTracking()
                    .Where(r => r.Id == targetId)
                    .Select(r => new { r.UserId })
                    .FirstOrDefaultAsync(cancellationToken);

                return review is null
                    ? VoteTarget.Invalid(Error.NotFound("reviews.not_found", "No existe esa reseña."))
                    : VoteTarget.Of(review.UserId);

            case LikeTargetType.Comment:
                var comment = await _context.Comments
                    .AsNoTracking()
                    .Where(c => c.Id == targetId)
                    .Select(c => new { c.IsDeleted, c.UserId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (comment is null)
                {
                    return VoteTarget.Invalid(
                        Error.NotFound("comments.not_found", "No existe ese comentario."));
                }

                return comment.IsDeleted
                    ? VoteTarget.Invalid(Error.Validation(
                        "comments.deleted", "No se puede votar un comentario borrado."))
                    : VoteTarget.Of(comment.UserId);

            default:
                return VoteTarget.Invalid(Error.Validation(
                    "likes.invalid_target", "El tipo de objetivo no es válido."));
        }
    }

    // ------------------------------------------------------------------
    // Avisos
    // ------------------------------------------------------------------

    private Task NotifyVoteAsync(
        LikeTargetType targetType,
        int targetId,
        Guid ownerId,
        Guid actorId,
        bool isLike,
        CancellationToken cancellationToken) =>
        targetType == LikeTargetType.Review
            ? _notifications.NotifyAsync(
                ownerId, actorId, NotificationType.ReviewVoted,
                reviewId: targetId, commentId: null, isLike: isLike,
                cancellationToken: cancellationToken)
            : _notifications.NotifyAsync(
                ownerId, actorId, NotificationType.CommentVoted,
                reviewId: null, commentId: targetId, isLike: isLike,
                cancellationToken: cancellationToken);

    private Task WithdrawVoteAsync(
        LikeTargetType targetType,
        int targetId,
        Guid actorId,
        CancellationToken cancellationToken) =>
        targetType == LikeTargetType.Review
            ? _notifications.WithdrawVoteNotificationAsync(
                actorId, NotificationType.ReviewVoted, reviewId: targetId, commentId: null,
                cancellationToken: cancellationToken)
            : _notifications.WithdrawVoteNotificationAsync(
                actorId, NotificationType.CommentVoted, reviewId: null, commentId: targetId,
                cancellationToken: cancellationToken);

    /// <summary>Conteos y voto propio en una sola consulta agregada.</summary>
    private async Task<VoteResultDto> GetVoteStateAsync(
        LikeTargetType targetType,
        int targetId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var state = await _context.Likes
            .Where(l => l.TargetType == targetType && l.TargetId == targetId)
            .GroupBy(l => 1)
            .Select(g => new
            {
                LikeCount = g.Count(l => l.IsLike),
                DislikeCount = g.Count(l => !l.IsLike),
                CurrentUserVote = g
                    .Where(l => l.UserId == userId)
                    .Select(l => (bool?)l.IsLike)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return state is null
            ? new VoteResultDto(0, 0, null)
            : new VoteResultDto(state.LikeCount, state.DislikeCount, state.CurrentUserVote);
    }

    private static Error Unauthenticated() =>
        Error.Unauthorized("auth.required", "Tenés que iniciar sesión.");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
