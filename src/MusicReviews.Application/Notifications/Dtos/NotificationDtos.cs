using MusicReviews.Application.Reviews.Dtos;
using MusicReviews.Domain.Enums;

namespace MusicReviews.Application.Notifications.Dtos;

/// <summary>
/// Aviso listo para mostrar. <c>Actor</c> es null en avisos del sistema; <c>ReviewId</c>
/// y <c>CommentId</c> son el destino del enlace y dependen del tipo.
/// </summary>
public sealed record NotificationDto(
    int Id,
    NotificationType Type,
    DateTimeOffset CreatedAt,
    bool IsRead,
    AuthorDto? Actor,
    int? ReviewId,
    int? CommentId,
    NotificationAlbumDto? Album,
    bool? IsLike);

/// <summary>Album de contexto, para que el aviso se entienda sin abrirlo.</summary>
public sealed record NotificationAlbumDto(
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    string ArtistName);

public sealed record UnreadCountDto(int Unread);
