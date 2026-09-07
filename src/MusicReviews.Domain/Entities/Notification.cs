using MusicReviews.Domain.Enums;

namespace MusicReviews.Domain.Entities;

/// <summary>
/// Aviso dirigido a un usuario sobre algo que otro hizo con su contenido.
/// </summary>
/// <remarks>
/// <para>
/// A diferencia del timeline de actividad —que se deriva de las tablas existentes— las
/// notificaciones si se persisten. La razon es que tienen estado propio que no vive en
/// ningun otro lado: si fueron leidas y cuando. Derivarlas obligaria a guardar igual una
/// marca de lectura por evento, que es exactamente esta tabla con mas pasos.
/// </para>
/// <para>
/// <see cref="ActorId"/> es nullable para dejar lugar a avisos del sistema (un
/// lanzamiento nuevo, una noticia) que no los genera ninguna persona.
/// </para>
/// </remarks>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Quien recibe el aviso.</summary>
    public Guid RecipientId { get; set; }
    public ApplicationUser Recipient { get; set; } = null!;

    /// <summary>Quien lo provoco. Null en avisos del sistema.</summary>
    public Guid? ActorId { get; set; }
    public ApplicationUser? Actor { get; set; }

    public NotificationType Type { get; set; }

    /// <summary>Reseña involucrada, cuando aplica. Es a donde lleva el aviso al abrirlo.</summary>
    public int? ReviewId { get; set; }
    public Review? Review { get; set; }

    /// <summary>Comentario involucrado, cuando aplica.</summary>
    public int? CommentId { get; set; }
    public Comment? Comment { get; set; }

    /// <summary>Para los votos: true si fue a favor. Null en el resto de los tipos.</summary>
    public bool? IsLike { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
