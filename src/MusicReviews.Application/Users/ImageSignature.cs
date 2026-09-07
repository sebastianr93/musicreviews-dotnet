namespace MusicReviews.Application.Users;

/// <summary>Formatos de imagen aceptados para un avatar.</summary>
public enum ImageFormat
{
    Unknown = 0,
    Jpeg = 1,
    Png = 2,
    Webp = 3,
    Gif = 4
}

/// <summary>
/// Reconoce el formato de una imagen por sus primeros bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no alcanza la extension ni el Content-Type.</b> Los dos los elige quien
/// sube el archivo. Un <c>.jpg</c> con <c>image/jpeg</c> declarado puede ser cualquier
/// cosa: un HTML con un script adentro, un SVG, un ejecutable. Como el avatar despues se
/// sirve desde nuestro dominio, un archivo que el navegador decida interpretar como HTML
/// es un XSS con nuestro origen. Los bytes, en cambio, no los negocia nadie.
/// </para>
/// <para>
/// <b>El SVG no esta en la lista a proposito.</b> Es un formato de imagen legitimo, pero
/// es XML y admite <c>&lt;script&gt;</c> adentro: servirlo desde el propio dominio es
/// exactamente el agujero que esto evita. Ademas no tiene firma binaria, con lo que ni
/// siquiera se podria reconocer de esta forma.
/// </para>
/// <para>
/// Esto <b>no</b> valida que la imagen este bien formada; para eso habria que decodificarla.
/// Lo que garantiza es que el navegador la va a tratar como imagen y no como documento,
/// que es lo que importa para la seguridad. La respuesta ademas se sirve con
/// <c>X-Content-Type-Options: nosniff</c>, para que no se replantee el tipo por su cuenta.
/// </para>
/// </remarks>
public static class ImageSignature
{
    /// <summary>Cuantos bytes hacen falta para decidir. WebP es el que mas pide: 12.</summary>
    public const int RequiredBytes = 12;

    public static ImageFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ImageFormat.Png;
        }

        // "RIFF" .... "WEBP": el tamanio va en el medio y no se mira.
        if (header.Length >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return ImageFormat.Webp;
        }

        // "GIF87a" o "GIF89a".
        if (header.Length >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46
            && header[3] == 0x38 && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return ImageFormat.Gif;
        }

        return ImageFormat.Unknown;
    }

    /// <summary>Extension con la que se guarda el archivo, segun lo que dicen los bytes.</summary>
    public static string ExtensionFor(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Png => ".png",
        ImageFormat.Webp => ".webp",
        ImageFormat.Gif => ".gif",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato no soportado.")
    };

    /// <summary>Content-Type con el que se sirve. Se deriva de los bytes, no de lo que declaro el cliente.</summary>
    public static string ContentTypeFor(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png => "image/png",
        ImageFormat.Webp => "image/webp",
        ImageFormat.Gif => "image/gif",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato no soportado.")
    };
}
