namespace MusicReviews.Application.Admin.Dtos;

/// <summary>Estadisticas basicas del sitio para el panel de administracion.</summary>
public sealed record AdminStatsDto(
    int TotalUsers,
    int NewUsersLast30Days,
    int ActiveUsersLast30Days,
    int TotalReviews,
    int TotalComments,
    int TotalVotes,
    int CachedArtists,
    int CachedAlbums,
    double? AverageScore,
    IReadOnlyList<MostReviewedAlbumDto> MostReviewedAlbums);

public sealed record MostReviewedAlbumDto(
    int AlbumId,
    string MusicBrainzId,
    string Title,
    string ArtistName,
    string? CoverArtUrl,
    int ReviewCount,
    double AverageScore);

/// <summary>Usuario visto desde el panel de administracion, con sus roles y actividad.</summary>
public sealed record AdminUserDto(
    Guid Id,
    string UserName,
    string Email,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles,
    int ReviewCount,
    int CommentCount,
    bool IsLockedOut);

public sealed record AssignRoleRequest(string Role);
