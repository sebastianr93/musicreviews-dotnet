using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Notifications;
using MusicReviews.Application.Users;
using MusicReviews.Application.Users.Dtos;
using MusicReviews.Domain.Entities;
using MusicReviews.Domain.Enums;
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
    private readonly INotificationWriter _notifications;
    private readonly IAvatarStorage _avatars;
    private readonly IOptionsMonitor<AvatarOptions> _avatarOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UserService> _logger;

    public UserService(
        AppDbContext context,
        IMusicCatalogService catalog,
        ICurrentUser currentUser,
        INotificationWriter notifications,
        IAvatarStorage avatars,
        IOptionsMonitor<AvatarOptions> avatarOptions,
        TimeProvider timeProvider,
        ILogger<UserService> logger)
    {
        _context = context;
        _catalog = catalog;
        _currentUser = currentUser;
        _notifications = notifications;
        _avatars = avatars;
        _avatarOptions = avatarOptions;
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
        // En variable local para que EF lo mande como parametro y no intente
        // traducir el acceso a la propiedad del servicio dentro de la expresion.
        var currentUserId = _currentUser.UserId;

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
                    : null,
                FollowerCount = u.Followers.Count(),
                FollowingCount = u.Following.Count(),
                IsFollowedByCurrentUser =
                    currentUserId != null && u.Followers.Any(f => f.FollowerId == currentUserId)
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
            favorites,
            profile.FollowerCount,
            profile.FollowingCount,
            profile.IsFollowedByCurrentUser,
            currentUserId == userId));
    }

    // ------------------------------------------------------------------
    // Avatar
    // ------------------------------------------------------------------

    public async Task<Result<UserProfileDto>> UpdateAvatarAsync(
        AvatarUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserProfileDto>(Unauthenticated());
        }

        var maxBytes = _avatarOptions.CurrentValue.MaxSizeBytes;

        if (upload.Length <= 0)
        {
            return Result.Failure<UserProfileDto>(Error.Validation(
                "users.avatar_empty", "El archivo está vacío."));
        }

        if (upload.Length > maxBytes)
        {
            return Result.Failure<UserProfileDto>(Error.Validation(
                "users.avatar_too_large",
                $"La imagen no puede superar los {maxBytes / (1024 * 1024)} MB."));
        }

        // Los primeros bytes deciden el formato. Ni la extension ni el Content-Type:
        // los dos los elige quien sube el archivo. Ver ImageSignature.
        var header = new byte[ImageSignature.RequiredBytes];
        var read = await ReadExactlyAsync(upload.Content, header, cancellationToken);
        var format = ImageSignature.Detect(header.AsSpan(0, read));

        if (format == ImageFormat.Unknown)
        {
            return Result.Failure<UserProfileDto>(Error.Validation(
                "users.avatar_unsupported",
                "El archivo no es una imagen JPG, PNG, WebP o GIF."));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserProfileDto>(UserNotFound());
        }

        // El encabezado ya se consumio del stream: se vuelve a poner adelante para que
        // el archivo se guarde completo.
        using var content = new MemoryStream();
        await content.WriteAsync(header.AsMemory(0, read), cancellationToken);
        await upload.Content.CopyToAsync(content, cancellationToken);
        content.Position = 0;

        var anterior = user.AvatarUrl;

        user.AvatarUrl = await _avatars.SaveAsync(userId, format, content, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        // El anterior se borra DESPUES de guardar: si el guardado falla, el usuario se
        // queda con la foto que tenia y no con ninguna.
        _avatars.DeleteIfOwned(anterior);

        _logger.LogInformation("Avatar actualizado por {UserId} ({Format})", userId, format);

        return await BuildProfileAsync(userId, cancellationToken);
    }

    public async Task<Result<UserProfileDto>> RemoveAvatarAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<UserProfileDto>(Unauthenticated());
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserProfileDto>(UserNotFound());
        }

        var anterior = user.AvatarUrl;
        user.AvatarUrl = null;

        await _context.SaveChangesAsync(cancellationToken);

        _avatars.DeleteIfOwned(anterior);

        return await BuildProfileAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Lee hasta llenar el buffer o hasta que el stream se termine.
    /// </summary>
    /// <remarks>
    /// Un solo <c>ReadAsync</c> puede devolver menos bytes de los pedidos aunque queden
    /// mas por leer —es contrato del tipo, no un caso raro— y con doce bytes pedidos
    /// sobre una subida de red pasa. Sin este bucle, una imagen valida se rechazaria
    /// porque el encabezado llego cortado.
    /// </remarks>
    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
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
    // ------------------------------------------------------------------
    // Seguimientos
    // ------------------------------------------------------------------

    public async Task<Result<UserProfileDto>> FollowAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } followerId)
        {
            return Result.Failure<UserProfileDto>(Unauthenticated());
        }

        var followedId = await FindUserIdAsync(userName, cancellationToken);

        if (followedId is null)
        {
            return Result.Failure<UserProfileDto>(UserNotFound());
        }

        if (followedId == followerId)
        {
            return Result.Failure<UserProfileDto>(Error.Validation(
                "users.cannot_follow_self", "No podés seguirte a vos mismo."));
        }

        var alreadyFollowing = await _context.UserFollows
            .AnyAsync(f => f.FollowerId == followerId && f.FollowedId == followedId, cancellationToken);

        if (!alreadyFollowing)
        {
            _context.UserFollows.Add(new UserFollow
            {
                FollowerId = followerId,
                FollowedId = followedId.Value,
                CreatedAt = _timeProvider.GetUtcNow()
            });

            // El aviso viaja en la misma transaccion que el seguimiento. Dejar de seguir
            // no lo retira: a diferencia de un voto, que es un estado que se muestra al
            // lado del contenido, "empezo a seguirte" es un hecho que ocurrio. Si el
            // usuario vuelve a seguir, el aviso sin leer se refresca en vez de duplicarse.
            await _notifications.NotifyAsync(
                followedId.Value,
                followerId,
                NotificationType.NewFollower,
                cancellationToken: cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Doble click sobre el boton: la PK compuesta corta al segundo y el
                // estado que corresponde devolver es el que ya quedo en la base.
                _logger.LogDebug(
                    "Seguimiento concurrente de {FollowerId} a {FollowedId}", followerId, followedId);

                _context.ChangeTracker.Clear();
            }
        }

        return await BuildProfileAsync(followedId.Value, cancellationToken);
    }

    public async Task<Result<UserProfileDto>> UnfollowAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } followerId)
        {
            return Result.Failure<UserProfileDto>(Unauthenticated());
        }

        var followedId = await FindUserIdAsync(userName, cancellationToken);

        if (followedId is null)
        {
            return Result.Failure<UserProfileDto>(UserNotFound());
        }

        await _context.UserFollows
            .Where(f => f.FollowerId == followerId && f.FollowedId == followedId)
            .ExecuteDeleteAsync(cancellationToken);

        return await BuildProfileAsync(followedId.Value, cancellationToken);
    }

    public Task<Result<PagedResult<FollowUserDto>>> GetFollowersAsync(
        string userName,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        QueryFollowsAsync(userName, page, followers: true, cancellationToken);

    public Task<Result<PagedResult<FollowUserDto>>> GetFollowingAsync(
        string userName,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        QueryFollowsAsync(userName, page, followers: false, cancellationToken);

    /// <summary>
    /// Las dos direcciones comparten toda la logica salvo de que lado de la relacion
    /// se filtra y cual se proyecta, asi que se resuelven con la misma consulta.
    /// </summary>
    private async Task<Result<PagedResult<FollowUserDto>>> QueryFollowsAsync(
        string userName,
        PageRequest page,
        bool followers,
        CancellationToken cancellationToken)
    {
        var userId = await FindUserIdAsync(userName, cancellationToken);

        if (userId is null)
        {
            return Result.Failure<PagedResult<FollowUserDto>>(UserNotFound());
        }

        var currentUserId = _currentUser.UserId;

        var query = followers
            ? _context.UserFollows.Where(f => f.FollowedId == userId)
            : _context.UserFollows.Where(f => f.FollowerId == userId);

        var total = await query.CountAsync(cancellationToken);

        if (total == 0)
        {
            return Result.Success(PagedResult<FollowUserDto>.Empty(page.Page, page.PageSize));
        }

        var items = await query
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .Select(f => new
            {
                User = followers ? f.Follower : f.Followed,
                f.CreatedAt
            })
            .Select(x => new FollowUserDto(
                x.User.Id,
                x.User.UserName!,
                x.User.AvatarUrl,
                x.User.Bio,
                x.User.Reviews.Count(),
                x.CreatedAt,
                // "Yo tambien lo sigo": permite pintar el boton correcto en el listado
                // sin una consulta por fila.
                currentUserId != null && x.User.Followers.Any(f => f.FollowerId == currentUserId)))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<FollowUserDto>(items, page.Page, page.PageSize, total));
    }

    private async Task<Guid?> FindUserIdAsync(string userName, CancellationToken cancellationToken)
    {
        var normalized = userName.ToUpperInvariant();

        return await _context.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

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
        Error.Unauthorized("auth.required", "Tenés que iniciar sesión.");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
