namespace MusicReviews.Application.Users;

/// <summary>
/// Forma de las URL con las que se publican los avatares, y su lectura inversa.
/// </summary>
/// <remarks>
/// Vive aparte del almacenamiento porque la usan las dos puntas —quien guarda la imagen y
/// quien la sirve— y tienen que coincidir exactamente. Teniendo la construccion y el
/// parseo en el mismo archivo, no hay forma de cambiar una sin ver la otra.
/// </remarks>
public static class AvatarUrl
{
    /// <summary>Cantidad de caracteres del identificador en formato "N" (hexadecimal sin guiones).</summary>
    public const int IdLength = 32;

    /// <summary>Arma la URL publica de un avatar.</summary>
    public static string For(string requestPath, Guid avatarId, ImageFormat format) =>
        $"{requestPath.TrimEnd('/')}/{avatarId:N}{ImageSignature.ExtensionFor(format)}";

    /// <summary>
    /// Extrae el identificador de una URL propia. Devuelve <see langword="null"/> si la
    /// URL no tiene la forma esperada o si apunta a otro sitio.
    /// </summary>
    /// <remarks>
    /// Que devuelva null en vez de fallar es deliberado: <c>AvatarUrl</c> es un campo que
    /// puede contener una direccion externa puesta a mano, y eso no es un error, es un
    /// avatar que no administramos.
    /// </remarks>
    public static Guid? IdFrom(string requestPath, string? url)
    {
        var prefix = requestPath.TrimEnd('/') + "/";

        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return ParseFileName(url[prefix.Length..]);
    }

    /// <summary>
    /// Convierte "{32 hexadecimales}.png" en el identificador correspondiente.
    /// </summary>
    /// <remarks>
    /// Se valida el formato en vez de confiar porque este texto llega desde la URL. No hay
    /// riesgo de recorrido de directorios —no se toca el disco— pero si de convertir
    /// cualquier cadena en una consulta a la base: exigir el formato exacto deja el
    /// descarte antes de salir a buscar nada.
    /// </remarks>
    public static Guid? ParseFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var dot = fileName.IndexOf('.');
        var name = dot < 0 ? fileName : fileName[..dot];

        return name.Length == IdLength && Guid.TryParseExact(name, "N", out var id) ? id : null;
    }
}
