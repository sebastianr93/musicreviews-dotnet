using Microsoft.EntityFrameworkCore;
using MusicReviews.Application.Activity;
using MusicReviews.Application.Activity.Dtos;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Reviews.Dtos;
using MusicReviews.Domain.Enums;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure.Activity;

/// <inheritdoc cref="IActivityService"/>
/// <remarks>
/// <para>
/// <b>No hay tabla de actividad.</b> El timeline se deriva de las tablas que ya existen
/// —reseñas, comentarios y votos— en vez de mantener una tabla de feed escrita en cada
/// accion. Una tabla asi es mas rapida de leer, pero hay que sincronizarla en cada alta,
/// baja y edicion, y cualquier camino que se olvide de hacerlo deja el feed mostrando
/// contenido que ya no existe. Derivando, borrar una reseña borra su actividad sin que
/// nadie tenga que acordarse.
/// </para>
/// <para>
/// <b>Cuatro consultas acotadas, no un UNION.</b> Cada tipo de actividad se consulta por
/// separado pidiendo solo lo que puede entrar en la pagina, y la mezcla se hace en
/// memoria. Son cuatro consultas fijas —no crecen con la cantidad de resultados ni de
/// usuarios seguidos— y cada una usa su propio indice <c>(UserId, CreatedAt DESC)</c>.
/// Un UNION de proyecciones heterogeneas seria una sola consulta, pero deja al motor sin
/// poder usar esos indices para el ORDER BY global.
/// </para>
/// <para>
/// Los votos necesitan dos consultas porque <c>Like</c> es polimorfico: sin FK real, el
/// join contra reseñas y contra comentarios no puede hacerse en una sola.
/// </para>
/// </remarks>
internal sealed class ActivityService : IActivityService
{
    /// <summary>Cuanto texto de una reseña o comentario entra en el feed.</summary>
    private const int ExcerptLength = 280;

    /// <summary>
    /// Cuantas veces se reintenta ampliando el cupo por consulta cuando los empates de
    /// instante consumen las filas traidas. Tres intentos cuadruplican el cupo inicial:
    /// haria falta que decenas de actividades compartieran el microsegundo exacto para
    /// agotarlo, y en ese caso la pagina sale completa igual, solo que sin saber que hay
    /// una siguiente.
    /// </summary>
    private const int MaxQueryAttempts = 3;

    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ActivityService(AppDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<CursorPage<ActivityEntryDto>>> GetUserActivityAsync(
        string userName,
        CursorRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = userName.ToUpperInvariant();

        var userId = await _context.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return Result.Failure<CursorPage<ActivityEntryDto>>(
                Error.NotFound("users.not_found", "No existe ese usuario."));
        }

        return Result.Success(await BuildAsync([userId.Value], request, cancellationToken));
    }

    public async Task<Result<CursorPage<ActivityEntryDto>>> GetFollowingFeedAsync(
        CursorRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<CursorPage<ActivityEntryDto>>(
                Error.Unauthorized("auth.required", "Tenés que iniciar sesión."));
        }

        var followedIds = await _context.UserFollows
            .Where(f => f.FollowerId == userId)
            .Select(f => f.FollowedId)
            .ToListAsync(cancellationToken);

        if (followedIds.Count == 0)
        {
            return Result.Success(CursorPage<ActivityEntryDto>.Empty());
        }

        return Result.Success(await BuildAsync(followedIds, request, cancellationToken));
    }

    // ------------------------------------------------------------------
    // Armado del timeline
    // ------------------------------------------------------------------

    private async Task<CursorPage<ActivityEntryDto>> BuildAsync(
        IReadOnlyList<Guid> userIds,
        CursorRequest request,
        CancellationToken cancellationToken)
    {
        var cursor = ActivityCursor.TryDecode(request.Cursor);

        // El filtro por fecha es INCLUYENTE para no perder las entradas que comparten
        // el instante del cursor; a esas las descarta IsAfter, que ya puede mirar el Id.
        // Sin cursor, MaxValue deja pasar todo (Npgsql lo manda como 'infinity').
        var before = cursor?.CreatedAt ?? DateTimeOffset.MaxValue;

        var comparer = Comparer<ActivityEntryDto>.Create(ActivityCursor.Compare);

        // Cuantas filas pedirle a cada consulta.
        //
        // Se necesitan Limit+1 entradas NUEVAS para saber si hay pagina siguiente, pero
        // el filtro por fecha es incluyente, asi que siempre vuelve al menos la entrada
        // del propio cursor y IsAfter la descarta en memoria. Pedir Limit+1 dejaba el
        // resultado en Limit y el servicio concluia "no hay mas" con entradas todavia
        // sin entregar. El +2 compensa esa entrada; si ademas hay empates de instante,
        // el bucle vuelve a consultar con el doble.
        var take = request.Limit + 2;
        var ordered = new List<ActivityEntryDto>();

        for (var attempt = 0; attempt < MaxQueryAttempts; attempt++)
        {
            // Secuenciales y no con Task.WhenAll: EF Core no admite dos operaciones
            // simultaneas sobre el mismo DbContext y lanzaria en tiempo de ejecucion.
            var buckets = new List<List<ActivityEntryDto>>(4)
            {
                await QueryReviewsAsync(userIds, before, take, cancellationToken),
                await QueryCommentsAsync(userIds, before, take, cancellationToken),
                await QueryReviewVotesAsync(userIds, before, take, cancellationToken),
                await QueryCommentVotesAsync(userIds, before, take, cancellationToken)
            };

            ordered = buckets
                .SelectMany(bucket => bucket)
                .Where(entry => cursor is null || cursor.IsAfter(entry))
                .Order(comparer)
                .Take(request.Limit + 1)
                .ToList();

            // Ya alcanza para responder, o no hay nada mas que traer: ninguna consulta
            // lleno su cupo, asi que no quedan filas escondidas detras del LIMIT.
            var exhausted = buckets.TrueForAll(bucket => bucket.Count < take);

            if (ordered.Count > request.Limit || exhausted)
            {
                break;
            }

            take *= 2;
        }

        var hasMore = ordered.Count > request.Limit;
        var items = hasMore ? ordered[..request.Limit] : ordered;

        return new CursorPage<ActivityEntryDto>(
            items,
            hasMore && items.Count > 0
                ? ActivityCursor.From(items[^1]).Encode()
                : null);
    }

    private async Task<List<ActivityEntryDto>> QueryReviewsAsync(
        IReadOnlyList<Guid> userIds, DateTimeOffset before, int take, CancellationToken ct) =>
        await _context.Reviews
            .AsNoTracking()
            .Where(r => userIds.Contains(r.UserId) && r.CreatedAt <= before)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => new ActivityEntryDto(
                "review:" + r.Id,
                ActivityKind.ReviewPublished,
                r.CreatedAt,
                new AuthorDto(r.User.Id, r.User.UserName!, r.User.AvatarUrl),
                new ActivityAlbumDto(
                    r.Album.Id, r.Album.MusicBrainzId, r.Album.Title,
                    r.Album.CoverArtUrl, r.Album.Artist.Name),
                new ActivityReviewDto(
                    r.Id, r.Score, new AuthorDto(r.User.Id, r.User.UserName!, r.User.AvatarUrl)),
                r.Text.Length > ExcerptLength ? r.Text.Substring(0, ExcerptLength) : r.Text,
                null))
            .ToListAsync(ct);

    private async Task<List<ActivityEntryDto>> QueryCommentsAsync(
        IReadOnlyList<Guid> userIds, DateTimeOffset before, int take, CancellationToken ct) =>
        await _context.Comments
            .AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && !c.IsDeleted && c.CreatedAt <= before)
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Select(c => new ActivityEntryDto(
                "comment:" + c.Id,
                ActivityKind.CommentPublished,
                c.CreatedAt,
                new AuthorDto(c.User.Id, c.User.UserName!, c.User.AvatarUrl),
                new ActivityAlbumDto(
                    c.Review.Album.Id, c.Review.Album.MusicBrainzId, c.Review.Album.Title,
                    c.Review.Album.CoverArtUrl, c.Review.Album.Artist.Name),
                new ActivityReviewDto(
                    c.Review.Id,
                    c.Review.Score,
                    new AuthorDto(c.Review.User.Id, c.Review.User.UserName!, c.Review.User.AvatarUrl)),
                c.Text.Length > ExcerptLength ? c.Text.Substring(0, ExcerptLength) : c.Text,
                null))
            .ToListAsync(ct);

    /// <remarks>
    /// El join es explicito porque <c>Like</c> no tiene navegacion hacia <c>Review</c>:
    /// la relacion es polimorfica y no hay FK que EF pueda seguir.
    /// </remarks>
    private async Task<List<ActivityEntryDto>> QueryReviewVotesAsync(
        IReadOnlyList<Guid> userIds, DateTimeOffset before, int take, CancellationToken ct) =>
        await (from like in _context.Likes.AsNoTracking()
               where userIds.Contains(like.UserId)
                     && like.TargetType == LikeTargetType.Review
                     && like.CreatedAt <= before
               join review in _context.Reviews on like.TargetId equals review.Id
               orderby like.CreatedAt descending
               select new ActivityEntryDto(
                   "like:" + like.Id,
                   ActivityKind.ReviewVoted,
                   like.CreatedAt,
                   new AuthorDto(like.User.Id, like.User.UserName!, like.User.AvatarUrl),
                   new ActivityAlbumDto(
                       review.Album.Id, review.Album.MusicBrainzId, review.Album.Title,
                       review.Album.CoverArtUrl, review.Album.Artist.Name),
                   new ActivityReviewDto(
                       review.Id,
                       review.Score,
                       new AuthorDto(review.User.Id, review.User.UserName!, review.User.AvatarUrl)),
                   null,
                   like.IsLike))
            .Take(take)
            .ToListAsync(ct);

    private async Task<List<ActivityEntryDto>> QueryCommentVotesAsync(
        IReadOnlyList<Guid> userIds, DateTimeOffset before, int take, CancellationToken ct) =>
        await (from like in _context.Likes.AsNoTracking()
               where userIds.Contains(like.UserId)
                     && like.TargetType == LikeTargetType.Comment
                     && like.CreatedAt <= before
               join comment in _context.Comments on like.TargetId equals comment.Id
               where !comment.IsDeleted
               orderby like.CreatedAt descending
               select new ActivityEntryDto(
                   "like:" + like.Id,
                   ActivityKind.CommentVoted,
                   like.CreatedAt,
                   new AuthorDto(like.User.Id, like.User.UserName!, like.User.AvatarUrl),
                   new ActivityAlbumDto(
                       comment.Review.Album.Id, comment.Review.Album.MusicBrainzId,
                       comment.Review.Album.Title, comment.Review.Album.CoverArtUrl,
                       comment.Review.Album.Artist.Name),
                   new ActivityReviewDto(
                       comment.Review.Id,
                       comment.Review.Score,
                       new AuthorDto(
                           comment.Review.User.Id,
                           comment.Review.User.UserName!,
                           comment.Review.User.AvatarUrl)),
                   comment.Text.Length > ExcerptLength
                       ? comment.Text.Substring(0, ExcerptLength)
                       : comment.Text,
                   like.IsLike))
            .Take(take)
            .ToListAsync(ct);
}
