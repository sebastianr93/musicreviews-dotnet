namespace MusicReviews.Application.Users.Dtos;

/// <summary>
/// Perfil publico de un usuario, con sus estadisticas y artistas favoritos.
/// <c>AverageScore</c> es null mientras no haya escrito ninguna review.
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
    IReadOnlyList<FavoriteArtistDto> FavoriteArtists);

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
