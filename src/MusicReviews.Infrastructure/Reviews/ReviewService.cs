using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Reviews;
using MusicReviews.Application.Reviews.Dtos;
using MusicReviews.Domain.Constants;
using MusicReviews.Domain.Entities;
using MusicReviews.Domain.Enums;
using MusicReviews.Infrastructure.Persistence;
using Npgsql;

namespace MusicReviews.Infrastructure.Reviews;

/// <inheritdoc cref="IReviewService"/>
internal sealed class ReviewService : IReviewService
{
    private const string UniqueViolationSqlState = "23505";

    private readonly AppDbContext _context;
    private readonly IMusicCatalogService _catalog;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        AppDbContext context,
        IMusicCatalogService catalog,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<ReviewService> logger)
    {
        _context = context;
        _catalog = catalog;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Lecturas
    // ------------------------------------------------------------------

    public async Task<Result<ReviewDto>> GetByIdAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        var review = await _context.Reviews
            .AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(ToDto(_currentUser.UserId))
            .FirstOrDefaultAsync(cancellationToken);

        return review is null
            ? Result.Failure<ReviewDto>(NotFound())
            : Result.Success(review);
    }

    public async Task<Result<PagedResult<ReviewDto>>> GetByAlbumAsync(
        string albumMusicBrainzId,
        PageRequest page,
        ReviewSortOrder sort = ReviewSortOrder.Newest,
        CancellationToken cancellationToken = default)
    {
        var albumId = await _context.Albums
            .Where(a => a.MusicBrainzId == albumMusicBrainzId)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // El album no esta cacheado todavia, asi que no puede tener reviews.
        // Devolver una pagina vacia es mas util que un 404: el cliente esta viendo
        // la ficha del album y solo quiere saber que no hay nada escrito aun.
        if (albumId is null)
        {
            return Result.Success(PagedResult<ReviewDto>.Empty(page.Page, page.PageSize));
        }

        return Result.Success(await QueryPageAsync(
            _context.Reviews.Where(r => r.AlbumId == albumId),
            page,
            sort,
            cancellationToken));
    }

    public async Task<Result<PagedResult<ReviewDto>>> GetByUserAsync(
        string userName,
        PageRequest page,
        ReviewSortOrder sort = ReviewSortOrder.Newest,
        CancellationToken cancellationToken = default)
    {
        var normalized = userName.ToUpperInvariant();

        var userId = await _context.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return Result.Failure<PagedResult<ReviewDto>>(
                Error.NotFound("users.not_found", "No existe ese usuario."));
        }

        return Result.Success(await QueryPageAsync(
            _context.Reviews.Where(r => r.UserId == userId),
            page,
            sort,
            cancellationToken));
    }

    // ------------------------------------------------------------------
    // Escrituras
    // ------------------------------------------------------------------

    public async Task<Result<ReviewDto>> CreateAsync(
        CreateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<ReviewDto>(Unauthenticated());
        }

        // El album puede no estar cacheado: el usuario viene del buscador del catalogo.
        // Esto lo trae de MusicBrainz y lo persiste, para que la FK tenga a que apuntar.
        var albumResult = await _catalog.GetAlbumAsync(request.AlbumMusicBrainzId, cancellationToken);

        if (albumResult.IsFailure)
        {
            return Result.Failure<ReviewDto>(albumResult.Error);
        }

        var now = _timeProvider.GetUtcNow();

        var review = new Review
        {
            UserId = userId,
            AlbumId = albumResult.Value.Id,
            Score = request.Score,
            Text = request.Text.Trim(),
            CreatedAt = now
        };

        _context.Reviews.Add(review);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // El indice unico (UserId, AlbumId) es el que sostiene la regla, no un
            // SELECT previo: entre el chequeo y el insert cabe otra request del mismo
            // usuario (doble click, dos pestañas) y la base es la unica que puede
            // decidir sin condicion de carrera.
            _context.Entry(review).State = EntityState.Detached;

            return Result.Failure<ReviewDto>(Error.Conflict(
                "reviews.already_exists",
                "Ya escribiste una reseña de este álbum. Editala en vez de crear otra."));
        }

        _logger.LogInformation(
            "Review {ReviewId} creada por {UserId} sobre el album {AlbumId}",
            review.Id, userId, review.AlbumId);

        return await GetByIdAsync(review.Id, cancellationToken);
    }

    public async Task<Result<ReviewDto>> UpdateAsync(
        int reviewId,
        UpdateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<ReviewDto>(Unauthenticated());
        }

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
        {
            return Result.Failure<ReviewDto>(NotFound());
        }

        // Editar es solo del autor: un admin puede moderar borrando, no reescribiendo
        // lo que otro opino.
        if (review.UserId != userId)
        {
            return Result.Failure<ReviewDto>(Error.Forbidden(
                "reviews.not_owner",
                "Solo el autor puede editar su reseña."));
        }

        review.Score = request.Score;
        review.Text = request.Text.Trim();
        review.UpdatedAt = _timeProvider.GetUtcNow();

        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(review.Id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure(Unauthenticated());
        }

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review is null)
        {
            return Result.Failure(NotFound());
        }

        var isAdmin = _currentUser.IsInRole(AppRoles.Admin);

        if (review.UserId != userId && !isAdmin)
        {
            return Result.Failure(Error.Forbidden(
                "reviews.not_owner",
                "Solo el autor o un administrador pueden borrar esta reseña."));
        }

        // Los comentarios caen por cascada desde la FK. Los likes no: la relacion
        // es polimorfica y no tiene FK real, asi que hay que limpiarlos a mano o
        // quedan huerfanos apuntando a un target que ya no existe.
        var commentIds = await _context.Comments
            .Where(c => c.ReviewId == reviewId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        await _context.Likes
            .Where(l => (l.TargetType == LikeTargetType.Review && l.TargetId == reviewId)
                     || (l.TargetType == LikeTargetType.Comment && commentIds.Contains(l.TargetId)))
            .ExecuteDeleteAsync(cancellationToken);

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Review {ReviewId} borrada por {UserId} (admin: {IsAdmin})",
            reviewId, userId, isAdmin);

        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Proyeccion
    // ------------------------------------------------------------------

    /// <summary>
    /// Proyeccion de Review a DTO con todos los contadores resueltos como subconsultas
    /// correlacionadas.
    /// </summary>
    /// <remarks>
    /// Esta es la parte que evita el N+1 en los listados: los conteos de likes,
    /// dislikes y comentarios, y el voto del usuario actual, se calculan dentro de
    /// la MISMA sentencia SQL que trae las reviews. La alternativa ingenua seria
    /// cargar <c>Include(r => r.Comments)</c> y <c>Include</c> de likes para despues
    /// contar en memoria: eso trae miles de filas para producir cuatro numeros, y
    /// ademas multiplica las filas entre si (producto cartesiano) al incluir dos
    /// colecciones a la vez.
    ///
    /// Los indices que las sostienen son <c>(TargetType, TargetId, IsLike)</c> en
    /// Likes y <c>(ReviewId, CreatedAt)</c> en Comments.
    /// </remarks>
    private Expression<Func<Review, ReviewDto>> ToDto(Guid? currentUserId)
    {
        // Like no tiene navegacion hacia Review: la relacion es polimorfica
        // ((TargetType, TargetId) apunta a dos tablas) y por eso no hay FK ni
        // propiedad de navegacion que EF pueda seguir. El DbSet capturado funciona
        // como raiz de subconsulta y EF lo traduce a un subselect correlacionado
        // dentro de la misma sentencia.
        var likes = _context.Likes;

        return review => new ReviewDto(
            review.Id,
            review.Score,
            review.Text,
            review.CreatedAt,
            review.UpdatedAt,
            new AuthorDto(
                review.User.Id,
                review.User.UserName!,
                review.User.AvatarUrl),
            new ReviewAlbumDto(
                review.Album.Id,
                review.Album.MusicBrainzId,
                review.Album.Title,
                review.Album.CoverArtUrl,
                review.Album.Artist.Name,
                review.Album.Artist.MusicBrainzId),
            likes.Count(l => l.TargetType == LikeTargetType.Review
                          && l.TargetId == review.Id
                          && l.IsLike),
            likes.Count(l => l.TargetType == LikeTargetType.Review
                          && l.TargetId == review.Id
                          && !l.IsLike),
            review.Comments.Count(c => !c.IsDeleted),
            currentUserId == null
                ? null
                : likes
                    .Where(l => l.TargetType == LikeTargetType.Review
                             && l.TargetId == review.Id
                             && l.UserId == currentUserId)
                    .Select(l => (bool?)l.IsLike)
                    .FirstOrDefault());
    }

    private async Task<PagedResult<ReviewDto>> QueryPageAsync(
        IQueryable<Review> query,
        PageRequest page,
        ReviewSortOrder sort,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<ReviewDto>.Empty(page.Page, page.PageSize);
        }

        var items = await ApplySort(query, sort)
            .AsNoTracking()
            .Skip(page.Offset)
            .Take(page.PageSize)
            .Select(ToDto(_currentUser.UserId))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReviewDto>(items, page.Page, page.PageSize, total);
    }

    private IQueryable<Review> ApplySort(IQueryable<Review> query, ReviewSortOrder sort)
    {
        var likes = _context.Likes;

        return sort switch
        {
            ReviewSortOrder.Oldest =>
                query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),

            ReviewSortOrder.HighestScore =>
                query.OrderByDescending(r => r.Score).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),

            ReviewSortOrder.LowestScore =>
                query.OrderBy(r => r.Score).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),

            ReviewSortOrder.MostLiked =>
                query
                    .OrderByDescending(r =>
                        likes.Count(l => l.TargetType == LikeTargetType.Review && l.TargetId == r.Id && l.IsLike)
                        - likes.Count(l => l.TargetType == LikeTargetType.Review && l.TargetId == r.Id && !l.IsLike))
                    .ThenByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.Id),

            // Newest y cualquier valor desconocido. El desempate por Id no es cosmetico:
            // sin un orden total, dos filas con el mismo CreatedAt pueden repetirse
            // entre paginas o no aparecer en ninguna.
            _ => query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
        };
    }

    private static Error NotFound() =>
        Error.NotFound("reviews.not_found", "No existe esa reseña.");

    private static Error Unauthenticated() =>
        Error.Unauthorized("auth.required", "Tenés que iniciar sesión.");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
