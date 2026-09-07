namespace MusicReviews.Domain.Enums;

/// <summary>
/// Motivo por el que se genero una notificacion. Se persiste como <c>int</c> con valores
/// explicitos: reordenar el enum no puede cambiar el significado de lo ya guardado.
/// </summary>
public enum NotificationType
{
    /// <summary>Alguien comento una reseña tuya.</summary>
    ReviewCommented = 1,

    /// <summary>Alguien respondio un comentario tuyo.</summary>
    CommentReplied = 2,

    /// <summary>Alguien voto una reseña tuya.</summary>
    ReviewVoted = 3,

    /// <summary>Alguien voto un comentario tuyo.</summary>
    CommentVoted = 4,

    /// <summary>Alguien empezo a seguirte.</summary>
    NewFollower = 5
}
