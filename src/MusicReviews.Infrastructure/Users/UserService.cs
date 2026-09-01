using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Users;
using MusicReviews.Application.Users.Dtos;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Persistence;
using Npgsql;

namespace MusicReviews.Infrastructure.Users;

/// <inheritdoc cref="IUserService"/>
internal sealed class UserService : IUserService
{
    private const string UniqueViolationSqlState = "23505";

    private readonly AppDbContext _context;
    private readonly IMusicCatalogService _catalog;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UserService> _logger;

    public UserService(
        AppDbContext context,
        IMusicCatalogService catalog,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<UserService> logger)
    {
        _context = context;
        _catalog = catalog;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Perfil
    // ------------------------------------------------------------------

    public async Task<Result<UserProfileDto>> GetProfileAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var normalized = userName.ToUpperInvariant();

        var userId = await _context.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return userId is null
            ? Result.Failure<UserProfileDto>(UserNotFound())
            : await BuildProfileAsync(userId.Value, cancellationToken);
    }

    public async Task<Result<UserProfileDto>> GetMyProfileAsync(CancellationToken cancellationToken = default)
    {
        return _currentUser.UserId is not { } userId
            ? Result.Failure<UserProfileDto>(Unauthenticated())
            : await BuildProfileAsync(userId, cancellationToken);
    }

    public async Task<Result<UserProfileDto>> UpdateMyProfileAsync(
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserProfileDto>(Unauthenticated());
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserProfileDto>(UserNotFound());
        }

        user.Bio = Normalize(request.Bio);
        user.AvatarUrl = Normalize(request.AvatarUrl);

        await _context.SaveChangesAsync(cancellationToken);

        return await BuildProfileAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Arma el perfil con dos consultas: una para los datos y contadores del usuario
    /// (subconsultas agregadas en la misma sentencia) y otra para los favoritos, que
    /// son una lista y no un escalar.
    /// </summary>
    private async Task<Result<UserProfileDto>> BuildProfileAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var profile = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Bio,
                u.AvatarUrl,
                u.CreatedAt,
                ReviewCount = u.Reviews.Count(),
                CommentCount = u.Comments.Count(c => !c.IsDeleted),
                AverageScore = u.Reviews.Any()
                    ? (double?)u.Reviews.Average(r => r.Score)
                    : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return Result.Failure<UserProfileDto>(UserNotFound());
        }

        var favorites = await QueryFavorites(userId).ToListAsync(cancellationToken);

        return Result.Success(new UserProfileDto(
            profile.Id,
            profile.UserName ?? string.Empty,
            profile.Bio,
            profile.AvatarUrl,
            profile.CreatedAt,
            profile.ReviewCount,
            profile.CommentCount,
            profile.AverageScore,
            favorites));
    }

    // ------------------------------------------------------------------
    // Favoritos
    // ------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<FavoriteArtistDto>>> GetFavoritesAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var normalized = userName.ToUpperInvariant();

        var userId = await _context.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return Result.Failure<IReadOnlyList<FavoriteArtistDto>>(UserNotFound());
        }

        var favorites = await QueryFavorites(userId.Value).ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<FavoriteArtistDto>>(favorites);
    }

    public async Task<Result<FavoriteArtistDto>> AddFavoriteAsync(
        AddFavoriteArtistRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<FavoriteArtistDto>(Unauthenticated());
        }

        // El artista puede no estar cacheado: el usuario viene del buscador del catalogo.
        var artistResult = await _catalog.GetArtistAsync(request.ArtistMusicBrainzId, cancellationToken);

        if (artistResult.IsFailure)
        {
            return Result.Failure<FavoriteArtistDto>(artistResult.Error);
        }

        var artistId = artistResult.Value.Id;

        var alreadyFavorite = await _context.FavoriteArtists
            .AnyAsync(f => f.UserId == userId && f.ArtistId == artistId, cancellationToken);

        if (!alreadyFavorite)
        {
            _context.FavoriteArtists.Add(new FavoriteArtist
            {
                UserId = userId,
                ArtistId = artistId,
                CreatedAt = _timeProvider.GetUtcNow()
            });

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // La PK compuesta (UserId, ArtistId) ya impide el duplicado; si dos
                // requests entran a la vez, la segunda simplemente no agrega nada.
                _logger.LogDebug(
                    "Favorito concurrente de {UserId} sobre el artista {ArtistId}", userId, artistId);

                _context.ChangeTracker.Clear();
            }
        }

        var favorite = await QueryFavorites(userId, artistId)
            .FirstOrDefaultAsync(cancellationToken);

        return favorite is null
            ? Result.Failure<FavoriteArtistDto>(Error.Failure(
                "users.favorite_failed", "No se pudo guardar el favorito."))
            : Result.Success(favorite);
    }

    public async Task<Result> RemoveFavoriteAsync(
        string artistMusicBrainzId,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure(Unauthenticated());
        }

        // Se resuelve contra la cache local: quitar un favorito nunca deberia
        // disparar una llamada a MusicBrainz.
        var artistId = await _context.Artists
            .Where(a => a.MusicBrainzId == artistMusicBrainzId)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (artistId is null)
        {
            // Si el artista no esta cacheado, no puede estar en favoritos de nadie.
            return Result.Success();
        }

        await _context.FavoriteArtists
            .Where(f => f.UserId == userId && f.ArtistId == artistId)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Consulta de favoritos. El filtro por artista se aplica ANTES de proyectar:
    /// <see cref="FavoriteArtistDto"/> es un record posicional, y EF no puede traducir
    /// un predicado sobre las propiedades de un objeto construido por constructor
    /// (no tiene como mapearlas de vuelta a columnas). Filtrar despues del Select
    /// falla en tiempo de ejecucion con "could not be translated".
    /// </summary>
    private IQueryable<FavoriteArtistDto> QueryFavorites(Guid userId, int? artistId = null)
    {
        var query = _context.FavoriteArtists
            .AsNoTracking()
            .Where(f => f.UserId == userId);

        if (artistId is { } id)
        {
            query = query.Where(f => f.ArtistId == id);
        }

        return query
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.ArtistId)
            .Select(f => new FavoriteArtistDto(
                f.Artist.Id,
                f.Artist.MusicBrainzId,
                f.Artist.Name,
                f.Artist.Disambiguation,
                f.Artist.ImageUrl,
                f.CreatedAt));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error UserNotFound() =>
        Error.NotFound("users.not_found", "No existe ese usuario.");

    private static Error Unauthenticated() =>
        Error.Unauthorized("auth.required", "Tenes que iniciar sesion.");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
