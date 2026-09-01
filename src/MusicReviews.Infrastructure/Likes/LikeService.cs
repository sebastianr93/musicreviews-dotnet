using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Likes;
using MusicReviews.Application.Likes.Dtos;
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LikeService> _logger;

    public LikeService(
        AppDbContext context,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<LikeService> logger)
    {
        _context = context;
        _currentUser = currentUser;
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

        var targetError = await ValidateTargetAsync(targetType, targetId, cancellationToken);

        if (targetError is not null)
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

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Dos clicks simultaneos sobre el mismo boton. El indice unico
                // (UserId, TargetType, TargetId) impide el voto duplicado; el estado
                // que corresponde devolver es el que quedo en la base.
                _logger.LogDebug(
                    "Voto concurrente de {UserId} sobre {TargetType} {TargetId}",
                    userId, targetType, targetId);

                _context.ChangeTracker.Clear();
            }
        }
        else if (existing.IsLike == request.IsLike)
        {
            // Mismo voto: es un toggle, se retira.
            _context.Likes.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Voto contrario: se cambia en lugar de crear otra fila.
            existing.IsLike = request.IsLike;
            existing.CreatedAt = _timeProvider.GetUtcNow();
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

        var targetError = await ValidateTargetAsync(targetType, targetId, cancellationToken);

        if (targetError is not null)
        {
            return Result.Failure<VoteResultDto>(targetError);
        }

        await _context.Likes
            .Where(l => l.UserId == userId && l.TargetType == targetType && l.TargetId == targetId)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success(await GetVoteStateAsync(targetType, targetId, userId, cancellationToken));
    }

    /// <summary>
    /// Verifica que el target exista. Es necesario justamente porque la relacion es
    /// polimorfica: no hay FK que rechace un voto sobre un id inexistente, asi que
    /// sin esta comprobacion la tabla acumularia votos apuntando a la nada.
    /// </summary>
    private async Task<Error?> ValidateTargetAsync(
        LikeTargetType targetType,
        int targetId,
        CancellationToken cancellationToken)
    {
        switch (targetType)
        {
            case LikeTargetType.Review:
                var reviewExists = await _context.Reviews
                    .AnyAsync(r => r.Id == targetId, cancellationToken);

                return reviewExists
                    ? null
                    : Error.NotFound("reviews.not_found", "No existe esa review.");

            case LikeTargetType.Comment:
                var comment = await _context.Comments
                    .Where(c => c.Id == targetId)
                    .Select(c => new { c.IsDeleted })
                    .FirstOrDefaultAsync(cancellationToken);

                if (comment is null)
                {
                    return Error.NotFound("comments.not_found", "No existe ese comentario.");
                }

                return comment.IsDeleted
                    ? Error.Validation("comments.deleted", "No se puede votar un comentario borrado.")
                    : null;

            default:
                return Error.Validation("likes.invalid_target", "El tipo de objetivo no es valido.");
        }
    }

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
        Error.Unauthorized("auth.required", "Tenes que iniciar sesion.");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
