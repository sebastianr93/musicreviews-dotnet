using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Users.Dtos;

namespace MusicReviews.Application.Users;

public interface IUserService
{
    /// <summary>Perfil publico de cualquier usuario, por nombre de usuario.</summary>
    Task<Result<UserProfileDto>> GetProfileAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>Perfil del usuario autenticado.</summary>
    Task<Result<UserProfileDto>> GetMyProfileAsync(CancellationToken cancellationToken = default);

    Task<Result<UserProfileDto>> UpdateMyProfileAsync(
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<FavoriteArtistDto>>> GetFavoritesAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega un artista a favoritos. Si no estaba en el catalogo local, se trae
    /// de MusicBrainz y se persiste primero. Idempotente.
    /// </summary>
    Task<Result<FavoriteArtistDto>> AddFavoriteAsync(
        AddFavoriteArtistRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Quita un artista de favoritos. Idempotente.</summary>
    Task<Result> RemoveFavoriteAsync(
        string artistMusicBrainzId,
        CancellationToken cancellationToken = default);
}
