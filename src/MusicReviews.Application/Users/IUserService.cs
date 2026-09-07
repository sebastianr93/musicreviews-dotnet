using MusicReviews.Application.Common.Models;
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

    /// <summary>
    /// Reemplaza el avatar del usuario por el archivo subido.
    /// </summary>
    /// <remarks>
    /// El archivo se valida por sus bytes, no por la extension ni por el Content-Type
    /// que declaro el cliente. Ver <see cref="ImageSignature"/>.
    /// </remarks>
    Task<Result<UserProfileDto>> UpdateAvatarAsync(
        AvatarUpload upload,
        CancellationToken cancellationToken = default);

    /// <summary>Quita el avatar subido y vuelve a la inicial.</summary>
    Task<Result<UserProfileDto>> RemoveAvatarAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// El usuario autenticado empieza a seguir a otro. Idempotente.
    /// Falla si intenta seguirse a si mismo.
    /// </summary>
    Task<Result<UserProfileDto>> FollowAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>Deja de seguir. Idempotente.</summary>
    Task<Result<UserProfileDto>> UnfollowAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>Quienes siguen a este usuario, mas recientes primero.</summary>
    Task<Result<PagedResult<FollowUserDto>>> GetFollowersAsync(
        string userName,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>A quienes sigue este usuario, mas recientes primero.</summary>
    Task<Result<PagedResult<FollowUserDto>>> GetFollowingAsync(
        string userName,
        PageRequest page,
        CancellationToken cancellationToken = default);
}
