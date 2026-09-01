namespace MusicReviews.Application.Likes.Dtos;

/// <summary>
/// Voto a aplicar. Es un toggle: si el usuario ya emitio el mismo voto, se retira;
/// si emitio el contrario, se cambia; si no voto, se crea.
/// </summary>
public sealed record ToggleVoteRequest(bool IsLike);

/// <summary>
/// Estado de la votacion despues de la operacion, para que el cliente actualice los
/// contadores sin pedir el recurso de nuevo. <c>CurrentUserVote</c> queda en null si
/// el usuario retiro su voto.
/// </summary>
public sealed record VoteResultDto(
    int LikeCount,
    int DislikeCount,
    bool? CurrentUserVote);
