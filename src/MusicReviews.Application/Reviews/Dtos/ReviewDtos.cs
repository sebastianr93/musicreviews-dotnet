namespace MusicReviews.Application.Reviews.Dtos;

/// <summary>
/// Alta de una review. El album se identifica por su MBID, no por el Id local:
/// el cliente viene del catalogo y puede estar reseniando un album que todavia
/// nadie cacheo. El servicio se encarga de traerlo y persistirlo antes de insertar.
/// </summary>
public sealed record CreateReviewRequest(
    string AlbumMusicBrainzId,
    int Score,
    string Text);

public sealed record UpdateReviewRequest(
    int Score,
    string Text);

/// <summary>Autor de una review o comentario, con lo minimo para renderizar la firma.</summary>
public sealed record AuthorDto(
    Guid Id,
    string UserName,
    string? AvatarUrl);

/// <summary>Referencia al album dentro de una review, para no tener que pedirlo aparte.</summary>
public sealed record ReviewAlbumDto(
    int Id,
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    string ArtistName,
    string ArtistMusicBrainzId);

/// <summary>
/// Review con sus contadores. Los tres contadores y el voto propio se resuelven
/// en la misma query que trae la review: ver <c>ReviewService</c>.
/// <c>CurrentUserVote</c> es true si el usuario autenticado dio like, false si dio
/// dislike y null si no voto (o si la request es anonima).
/// </summary>
public sealed record ReviewDto(
    int Id,
    int Score,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    AuthorDto Author,
    ReviewAlbumDto Album,
    int LikeCount,
    int DislikeCount,
    int CommentCount,
    bool? CurrentUserVote);

/// <summary>Criterio de orden de los listados de reviews.</summary>
public enum ReviewSortOrder
{
    /// <summary>Mas recientes primero. Por defecto.</summary>
    Newest = 0,
    Oldest = 1,
    HighestScore = 2,
    LowestScore = 3,
    /// <summary>Mas votadas primero (likes menos dislikes).</summary>
    MostLiked = 4
}
