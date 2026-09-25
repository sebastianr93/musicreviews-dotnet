namespace MusicReviews.Application.Users;

/// <summary>
/// Guarda y borra los avatares subidos.
/// </summary>
/// <remarks>
/// Es una interfaz y no una llamada directa al sistema de archivos porque el "donde" es
/// una decision de infraestructura: hoy son filas en la base, en un despliegue mas
/// grande seria un bucket de objetos. Los casos de uso no deberian enterarse.
/// </remarks>
public interface IAvatarStorage
{
    /// <summary>
    /// Prepara el guardado de la imagen y devuelve la URL publica con la que se sirve.
    /// </summary>
    /// <remarks>
    /// <b>El metodo NO guarda: deja la escritura pendiente y la confirma quien llama.</b>
    /// Es a proposito. El mismo <c>SaveChanges</c> que persiste la imagen persiste el
    /// <c>AvatarUrl</c> del usuario que la apunta, asi que o quedan las dos cosas o no
    /// queda ninguna. Guardando por separado existe la ventana en la que el perfil apunta
    /// a una imagen que no llego a escribirse.
    /// </remarks>
    Task<string> SaveAsync(
        Guid userId,
        ImageFormat format,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra un avatar anterior, si la URL apunta a uno nuestro. Este si escribe solo.
    /// </summary>
    /// <remarks>
    /// Ignora en silencio las URL que no administramos —un avatar puesto a mano
    /// apuntando a otro sitio— y las que ya no existen. Es limpieza: que falle no puede
    /// impedir que el usuario cambie su foto.
    /// </remarks>
    Task DeleteIfOwnedAsync(string? url, CancellationToken cancellationToken = default);
}

/// <summary>Motivos por los que un avatar puede rechazarse.</summary>
public enum AvatarRejection
{
    None = 0,
    Empty = 1,
    TooLarge = 2,
    UnsupportedFormat = 3
}
