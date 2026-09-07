using MusicReviews.Application.Reviews.Dtos;

namespace MusicReviews.Application.Activity.Dtos;

/// <summary>Que hizo el usuario.</summary>
public enum ActivityKind
{
    /// <summary>Publico una reseña.</summary>
    ReviewPublished = 1,

    /// <summary>Comento una reseña.</summary>
    CommentPublished = 2,

    /// <summary>Voto una reseña.</summary>
    ReviewVoted = 3,

    /// <summary>Voto un comentario.</summary>
    CommentVoted = 4
}

/// <summary>Album al que apunta una actividad.</summary>
public sealed record ActivityAlbumDto(
    int Id,
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    string ArtistName);

/// <summary>
/// Reseña involucrada en la actividad: la que se publico, la que se comento o la que
/// se voto. <c>Author</c> puede ser distinto del actor de la actividad — es justamente
/// el caso cuando alguien vota o comenta la reseña de otro.
/// </summary>
public sealed record ActivityReviewDto(
    int Id,
    int Score,
    AuthorDto Author);

/// <summary>
/// Una entrada del timeline.
/// </summary>
/// <remarks>
/// Los campos opcionales dependen de <see cref="Kind"/>: una reseña publicada trae
/// album, reseña y extracto; un voto trae ademas <see cref="IsLike"/> y ningun
/// extracto. Se eligio un unico DTO con campos nulos en vez de una jerarquia por tipo
/// porque el consumidor es un feed que renderiza todo en la misma lista: con jerarquia,
/// el cliente tendria que discriminar el tipo del payload antes de poder leer siquiera
/// la fecha.
///
/// <c>Id</c> es estable y unico entre tipos ("review:12", "like:98"): sirve como clave
/// de render en el cliente y como parte del cursor de paginacion.
/// </remarks>
public sealed record ActivityEntryDto(
    string Id,
    ActivityKind Kind,
    DateTimeOffset CreatedAt,
    AuthorDto Actor,
    ActivityAlbumDto? Album,
    ActivityReviewDto? Review,
    string? Excerpt,
    bool? IsLike);
