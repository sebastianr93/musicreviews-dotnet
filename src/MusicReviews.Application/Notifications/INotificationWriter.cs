using MusicReviews.Domain.Enums;

namespace MusicReviews.Application.Notifications;

/// <summary>
/// Genera avisos desde los servicios que provocan los eventos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Los metodos NO guardan.</b> Solo encolan el cambio en el contexto; el
/// <c>SaveChangesAsync</c> lo hace el servicio que provoco el evento, que es el que sabe
/// en que momento su propia operacion quedo consistente. En los votos y los seguimientos
/// eso permite que el aviso viaje en la misma transaccion que la accion. En los
/// comentarios hace falta un guardado previo para obtener el Id que genera la base, asi
/// que el aviso va en una segunda escritura: si esa fallara, el comentario queda
/// publicado y solo se pierde el aviso, que es la degradacion correcta.
/// </para>
/// <para>
/// Se separo de <c>INotificationService</c> a proposito: los servicios de comentarios y
/// votos solo necesitan escribir, y depender de la interfaz de lectura les daria acceso
/// a operaciones que no les corresponden —como marcar avisos ajenos como leidos—.
/// </para>
/// </remarks>
public interface INotificationWriter
{
    /// <summary>
    /// Encola un aviso, salvo que el destinatario sea el propio actor: nadie necesita
    /// que le avisen de lo que acaba de hacer.
    /// </summary>
    /// <param name="isLike">Solo para los tipos de voto.</param>
    Task NotifyAsync(
        Guid recipientId,
        Guid actorId,
        NotificationType type,
        int? reviewId = null,
        int? commentId = null,
        bool? isLike = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retira el aviso todavia no leido que corresponde a un voto que se deshizo.
    /// Sin esto, poner y sacar un voto repetidamente llenaria la campana de avisos
    /// de algo que ya no es cierto.
    /// </summary>
    Task WithdrawVoteNotificationAsync(
        Guid actorId,
        NotificationType type,
        int? reviewId,
        int? commentId,
        CancellationToken cancellationToken = default);
}
