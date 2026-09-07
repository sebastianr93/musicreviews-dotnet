namespace MusicReviews.Application.Users;

/// <summary>
/// Guarda y borra los avatares subidos.
/// </summary>
/// <remarks>
/// Es una interfaz y no una llamada directa al sistema de archivos porque el "donde" es
/// una decision de infraestructura que va a cambiar: hoy es una carpeta en disco, en un
/// despliegue con varias instancias tendria que ser almacenamiento compartido o un
/// bucket. Los casos de uso no deberian enterarse.
/// </remarks>
public interface IAvatarStorage
{
    /// <summary>
    /// Guarda la imagen y devuelve la URL publica con la que se sirve.
    /// </summary>
    Task<string> SaveAsync(
        Guid userId,
        ImageFormat format,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra el archivo de un avatar anterior, si la URL apunta a uno nuestro.
    /// </summary>
    /// <remarks>
    /// Ignora en silencio las URL que no administramos —un avatar puesto a mano
    /// apuntando a otro sitio— y las que ya no existen. Es limpieza: que falle no puede
    /// impedir que el usuario cambie su foto.
    /// </remarks>
    void DeleteIfOwned(string? url);
}

/// <summary>Motivos por los que un avatar puede rechazarse.</summary>
public enum AvatarRejection
{
    None = 0,
    Empty = 1,
    TooLarge = 2,
    UnsupportedFormat = 3
}
