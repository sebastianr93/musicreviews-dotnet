using MusicReviews.Application.Users;

namespace MusicReviews.UnitTests.Users;

/// <summary>
/// La forma de las URL de avatar y su lectura inversa.
/// </summary>
/// <remarks>
/// Se prueban juntas porque el riesgo es que se desincronicen: quien guarda arma la URL y
/// quien sirve la desarma, y si dejan de coincidir el sintoma es una foto que se sube bien
/// y despues no carga nunca.
/// </remarks>
public class AvatarUrlTests
{
    private const string RequestPath = "/avatars";

    [Fact]
    public void For_ArmaLaUrlConElIdSinGuionesYLaExtensionDelFormato()
    {
        var id = Guid.Parse("01a07765-a88f-777e-b3b4-a817f5b8c8a9");

        var url = AvatarUrl.For(RequestPath, id, ImageFormat.Png);

        Assert.Equal("/avatars/01a07765a88f777eb3b4a817f5b8c8a9.png", url);
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg, ".jpg")]
    [InlineData(ImageFormat.Png, ".png")]
    [InlineData(ImageFormat.Webp, ".webp")]
    [InlineData(ImageFormat.Gif, ".gif")]
    public void For_UsaLaExtensionQueCorrespondeAlFormato(ImageFormat format, string extension)
    {
        var url = AvatarUrl.For(RequestPath, Guid.NewGuid(), format);

        Assert.EndsWith(extension, url);
    }

    [Fact]
    public void For_NoDuplicaLaBarraSiElPrefijoYaTerminaEnUna()
    {
        var url = AvatarUrl.For("/avatars/", Guid.NewGuid(), ImageFormat.Png);

        Assert.DoesNotContain("//", url);
    }

    [Fact]
    public void IdFrom_RecuperaExactamenteElIdQueSeUsoParaArmarla()
    {
        // El unico test que realmente importa: ida y vuelta.
        var id = Guid.NewGuid();

        var url = AvatarUrl.For(RequestPath, id, ImageFormat.Webp);

        Assert.Equal(id, AvatarUrl.IdFrom(RequestPath, url));
    }

    [Theory]
    [InlineData("https://ejemplo.com/foto.png")]
    [InlineData("/otra-carpeta/01a07765a88f777eb3b4a817f5b8c8a9.png")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IdFrom_DevuelveNullSiLaUrlNoEsNuestra(string? url)
    {
        // Un avatar puede apuntar a un sitio externo: eso no es un error, es una imagen
        // que no administramos y que no hay que intentar borrar.
        Assert.Null(AvatarUrl.IdFrom(RequestPath, url));
    }

    [Theory]
    [InlineData("01a07765a88f777eb3b4a817f5b8c8a9.png")]
    [InlineData("01a07765a88f777eb3b4a817f5b8c8a9.jpg")]
    [InlineData("01a07765a88f777eb3b4a817f5b8c8a9")]
    public void ParseFileName_AceptaElFormatoEsperadoConOSinExtension(string fileName)
    {
        Assert.Equal(
            Guid.Parse("01a07765-a88f-777e-b3b4-a817f5b8c8a9"),
            AvatarUrl.ParseFileName(fileName));
    }

    [Theory]
    [InlineData("no-es-un-id.png")]
    [InlineData("01a07765-a88f-777e-b3b4-a817f5b8c8a9.png")]  // con guiones: no es el formato "N"
    [InlineData("01a07765a88f777eb3b4a817f5b8c8.png")]        // 30 caracteres
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseFileName_RechazaCualquierOtraCosa(string? fileName)
    {
        // Este texto llega desde la URL. Exigir el formato exacto descarta antes de
        // convertir una cadena arbitraria en una consulta a la base.
        Assert.Null(AvatarUrl.ParseFileName(fileName));
    }
}
