namespace MusicReviews.Application.Users.Dtos;

/// <summary>
/// Perfil publico de un usuario, con sus estadisticas y artistas favoritos.
/// <c>AverageScore</c> es null mientras no haya escrito ninguna review.
/// <c>IsFollowedByCurrentUser</c> viene en false para las consultas anonimas, e
/// <c>IsCurrentUser</c> le permite al cliente decidir si muestra "Seguir" o "Editar
/// perfil" sin tener que comparar ids por su cuenta.
/// </summary>
public sealed record UserProfileDto(
    Guid Id,
    string UserName,
    string? Bio,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    int ReviewCount,
    int CommentCount,
    double? AverageScore,
    IReadOnlyList<FavoriteArtistDto> FavoriteArtists,
    int FollowerCount = 0,
    int FollowingCount = 0,
    bool IsFollowedByCurrentUser = false,
    bool IsCurrentUser = false);

/// <summary>Usuario dentro de un listado de seguidores o seguidos.</summary>
public sealed record FollowUserDto(
    Guid Id,
    string UserName,
    string? AvatarUrl,
    string? Bio,
    int ReviewCount,
    DateTimeOffset FollowedAt,
    bool IsFollowedByCurrentUser);

public sealed record FavoriteArtistDto(
    int ArtistId,
    string MusicBrainzId,
    string Name,
    string? Disambiguation,
    string? ImageUrl,
    DateTimeOffset AddedAt);

/// <summary>
/// Edicion del perfil propio. Los campos en null se interpretan como "borrar el valor",
/// no como "dejarlo igual": es un PUT, reemplaza el recurso completo.
/// </summary>
public sealed record UpdateProfileRequest(
    string? Bio,
    string? AvatarUrl);

/// <summary>
/// El artista se identifica por MBID: puede no estar cacheado todavia, y el servicio
/// lo trae de MusicBrainz antes de guardarlo como favorito.
/// </summary>
public sealed record AddFavoriteArtistRequest(string ArtistMusicBrainzId);

/// <summary>
/// Un avatar recien subido, en terminos que la capa Application entiende.
/// </summary>
/// <remarks>
/// No es un <c>IFormFile</c> a proposito: ese tipo vive en ASP.NET Core y meterlo aca
/// ataria la capa de casos de uso al framework web. Con esto, la misma logica sirve si
/// maniana el avatar llega por otro camino.
/// </remarks>
public sealed record AvatarUpload(string FileName, long Length, Stream Content);

/// <summary>Cambio de contraseña. Pide la actual: tener la sesion abierta no alcanza.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
