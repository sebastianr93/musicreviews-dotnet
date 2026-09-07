using MusicReviews.Application.Users;

namespace MusicReviews.UnitTests.Users;

/// <summary>
/// El reconocimiento de formato de un avatar.
/// </summary>
/// <remarks>
/// Es la mitad de la defensa contra subir cualquier cosa disfrazada de imagen: la otra
/// mitad es el <c>nosniff</c> al servirla. Lo que estos tests fijan no es que reconozca
/// bien un JPEG —eso es lo facil— sino que <b>rechace</b> lo que no lo es, empezando por
/// el SVG, que es una imagen legitima pero puede llevar script adentro.
/// </remarks>
public class ImageSignatureTests
{
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static byte[] Webp() => [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public void Detect_ReconoceLosFormatosAceptados()
    {
        Assert.Equal(ImageFormat.Jpeg, ImageSignature.Detect(Jpeg()));
        Assert.Equal(ImageFormat.Png, ImageSignature.Detect(Png()));
        Assert.Equal(ImageFormat.Webp, ImageSignature.Detect(Webp()));
        Assert.Equal(ImageFormat.Gif, ImageSignature.Detect("GIF87a"u8));
        Assert.Equal(ImageFormat.Gif, ImageSignature.Detect("GIF89a"u8));
    }

    [Fact]
    public void Detect_RechazaElSvg()
    {
        // Un SVG es XML y admite <script> adentro. Servido desde el propio dominio, eso
        // es un XSS con nuestro origen: por eso no esta en la lista, aunque el navegador
        // lo considere una imagen.
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect("<svg xmlns"u8));
    }

    [Fact]
    public void Detect_RechazaUnHtmlDisfrazado()
    {
        // El caso que importa: alguien sube "foto.jpg" con Content-Type image/jpeg y
        // adentro hay una pagina. La extension y el Content-Type los eligio el atacante;
        // los bytes, no.
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect("<!DOCTYPE ht"u8));
    }

    [Fact]
    public void Detect_ConUnRiffQueNoEsWebp_NoLoAcepta()
    {
        // "RIFF" tambien encabeza los WAV y los AVI: sin comprobar los cuatro bytes del
        // medio, cualquiera de ellos pasaria por imagen.
        byte[] wav = [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45];

        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect(wav));
    }

    [Fact]
    public void Detect_ConMenosBytesDeLosNecesarios_NoAdivina()
    {
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect([]));
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect([0xFF]));

        // La firma del PNG son ocho bytes: con cuatro no alcanza para afirmarlo.
        Assert.Equal(ImageFormat.Unknown, ImageSignature.Detect([0x89, 0x50, 0x4E, 0x47]));
    }

    [Fact]
    public void Detect_ConLaFirmaJustaDelJpeg_LaReconoce()
    {
        // Tres bytes son toda la firma del JPEG: no hace falta esperar a los doce.
        Assert.Equal(ImageFormat.Jpeg, ImageSignature.Detect([0xFF, 0xD8, 0xFF]));
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg, ".jpg", "image/jpeg")]
    [InlineData(ImageFormat.Png, ".png", "image/png")]
    [InlineData(ImageFormat.Webp, ".webp", "image/webp")]
    [InlineData(ImageFormat.Gif, ".gif", "image/gif")]
    public void ExtensionYContentType_SalenDelFormatoDetectado(
        ImageFormat format, string extension, string contentType)
    {
        Assert.Equal(extension, ImageSignature.ExtensionFor(format));
        Assert.Equal(contentType, ImageSignature.ContentTypeFor(format));
    }

    [Fact]
    public void ExtensionFor_ConUnFormatoDesconocido_Lanza()
    {
        // Preferible una excepcion que guardar un archivo sin extension o con una
        // inventada: si se llego aca con Unknown, es un bug del llamador.
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSignature.ExtensionFor(ImageFormat.Unknown));
    }
}
