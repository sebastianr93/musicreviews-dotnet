using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// Las opciones de "Mi perfil": cambiar la contraseña y subir el avatar.
/// </summary>
public class AccountEndpointsTests : IntegrationTestBase
{
    public AccountEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record ProfilePayload(Guid Id, string UserName, string? AvatarUrl);

    /// <summary>PNG mínimo válido: lo que importa es la firma de ocho bytes.</summary>
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    // ------------------------------------------------------------------
    // Avatar
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubirAvatar_SinAutenticar_DevuelveUnauthorized()
    {
        var response = await CreateClient().PostAsync(
            "/api/users/me/avatar", FileContent(Png(), "foto.png", "image/png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubirAvatar_ConUnPng_DejaElPerfilApuntandoAlArchivo()
    {
        var user = await CreateUserAsync("av1");

        var response = await user.Client.PostAsync(
            "/api/users/me/avatar", FileContent(Png(), "foto.png", "image/png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        Assert.NotNull(profile!.AvatarUrl);
        Assert.StartsWith("/avatars/", profile.AvatarUrl);

        // La extensión sale de los bytes, no del nombre que mandó el cliente.
        Assert.EndsWith(".png", profile.AvatarUrl);
    }

    [Fact]
    public async Task SubirAvatar_ConUnArchivoQueNoEsImagen_LoRechaza()
    {
        // El caso que importa: extensión y Content-Type de imagen, contenido que no lo
        // es. Los dos los elige quien sube el archivo; los bytes, no.
        var user = await CreateUserAsync("av2");
        var html = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE html><script>alert(1)</script>");

        var response = await user.Client.PostAsync(
            "/api/users/me/avatar", FileContent(html, "foto.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("users.avatar_unsupported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubirAvatar_ConUnSvg_LoRechaza()
    {
        // Un SVG es una imagen legítima pero es XML y admite script adentro: servido
        // desde el propio dominio sería un XSS con nuestro origen.
        var user = await CreateUserAsync("av3");
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var response = await user.Client.PostAsync(
            "/api/users/me/avatar", FileContent(svg, "foto.svg", "image/svg+xml"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubirAvatar_QueSuperaElTope_LoRechaza()
    {
        var user = await CreateUserAsync("av4");

        // Encabezado de PNG válido seguido de relleno hasta pasar los 2 MB: así el
        // rechazo es por tamaño y no por formato.
        var grande = new byte[3 * 1024 * 1024];
        Png().CopyTo(grande, 0);

        var response = await user.Client.PostAsync(
            "/api/users/me/avatar", FileContent(grande, "foto.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("users.avatar_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task QuitarAvatar_LoDejaEnNull()
    {
        var user = await CreateUserAsync("av5");

        await user.Client.PostAsync("/api/users/me/avatar", FileContent(Png(), "foto.png", "image/png"));

        var response = await user.Client.DeleteAsync("/api/users/me/avatar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        Assert.Null(profile!.AvatarUrl);
    }

    // ------------------------------------------------------------------
    // Contraseña
    // ------------------------------------------------------------------

    [Fact]
    public async Task CambiarContrasenia_SinAutenticar_DevuelveUnauthorized()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = "TestUser1234",
            newPassword = "OtraClave5678"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CambiarContrasenia_ConLaActualIncorrecta_Falla()
    {
        var user = await CreateUserAsync("pw1");

        var response = await user.Client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = "NoEsLaMia9999",
            newPassword = "OtraClave5678"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CambiarContrasenia_ConUnaNuevaDebil_Falla()
    {
        // Las reglas de la nueva son las mismas del registro: si acá fueran más laxas,
        // cambiar la contraseña sería la forma de esquivarlas.
        var user = await CreateUserAsync("pw2");

        var response = await user.Client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = user.Password,
            newPassword = "corta"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CambiarContrasenia_RevocaTodasLasSesiones()
    {
        var user = await CreateUserAsync("pw3");
        const string nueva = "ClaveNueva9876";

        var response = await user.Client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = user.Password,
            newPassword = nueva
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Este es el punto del ejercicio: cambiar la contraseña se hace casi siempre
        // porque se sospecha que alguien más entró. Si el refresh token viejo siguiera
        // renovándose, el cambio no serviría para nada.
        var refresh = await CreateClient().PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = user.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Y la contraseña nueva sí entra.
        var login = await CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = user.Email,
            password = nueva
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
