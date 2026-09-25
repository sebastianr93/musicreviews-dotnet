using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicReviews.Application.Users;
using MusicReviews.Infrastructure.Users;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// El almacenamiento real de avatares: los bytes van a la base y salen por
/// <c>AvatarsController</c>.
/// </summary>
/// <remarks>
/// <para>
/// El resto de la suite usa <c>FakeAvatarStorage</c>, que devuelve una URL sin guardar
/// nada: alcanza para verificar el flujo de validacion y no ensucia la base con binarios
/// en cada test. Pero entonces <b>nadie ejercita el almacenamiento de verdad</b>, y es
/// justo la pieza que se reescribio para poder desplegar sin disco persistente.
/// </para>
/// <para>
/// Esta clase vuelve a poner la implementacion real. El caso que importa es el de punta a
/// punta: subir una imagen y despues poder descargarla por la URL que devolvio el perfil.
/// Si esos dos lados dejan de coincidir, el sintoma en produccion es una foto que se sube
/// bien y despues no carga nunca.
/// </para>
/// </remarks>
public class AvatarStorageTests : IntegrationTestBase
{
    /// <summary>
    /// Host con el almacenamiento real, construido una sola vez para toda la clase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WithWebHostBuilder</c> reconstruye el host aplicando un ajuste extra encima del
    /// de la factory compartida, asi que el reemplazo por el falso queda deshecho. La base
    /// es la misma: la configuracion que apunta al contenedor de PostgreSQL se hereda.
    /// </para>
    /// <para>
    /// Es estatico porque xUnit crea una instancia de la clase por test, y levantar el
    /// host completo diez veces sumaria segundos sin aislar nada. Es seguro porque todas
    /// las clases de test viven en la misma coleccion y por lo tanto no corren en
    /// paralelo (ver <c>ApiTestCollection</c>).
    /// </para>
    /// </remarks>
    private static WebApplicationFactory<Program>? _realStorageHost;

    private readonly WebApplicationFactory<Program> _factory;

    public AvatarStorageTests(MusicReviewsApiFactory factory) : base(factory)
    {
        _factory = _realStorageHost ??= factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAvatarStorage>();
                services.AddScoped<IAvatarStorage, DbAvatarStorage>();
            }));
    }

    /// <summary>PNG minimo valido: la firma de ocho bytes mas un relleno reconocible.</summary>
    private static byte[] Png() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02];

    private static byte[] Gif() =>
        [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x10, 0x00, 0x10, 0x00, 0x80, 0x00, 0x11, 0x22];

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private sealed record ProfilePayload(Guid Id, string UserName, string? AvatarUrl);

    /// <summary>Registra un usuario contra el host con almacenamiento real.</summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var userName = $"{prefix}{Guid.NewGuid():N}"[..20];

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email = $"{userName}@musicreviews.tests",
            password = "TestUser1234"
        });

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(Json);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }

    private async Task<string> UploadAsync(HttpClient client, byte[] bytes, string fileName, string contentType)
    {
        var response = await client.PostAsync("/api/users/me/avatar", FileContent(bytes, fileName, contentType));

        response.EnsureSuccessStatusCode();

        var profile = await response.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        Assert.NotNull(profile!.AvatarUrl);

        return profile.AvatarUrl!;
    }

    [Fact]
    public async Task LaImagenSubidaSeDescargaConLosMismosBytes()
    {
        // Este es el test que justifica la clase: la URL que devuelve el perfil tiene que
        // servir exactamente el archivo que se subio.
        var client = await CreateAuthenticatedClientAsync("st1");
        var original = Png();

        var url = await UploadAsync(client, original, "foto.png", "image/png");

        var download = await _factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(original, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ElAvatarEsPublico()
    {
        // Se ve en cualquier perfil publico: pedirlo sin token tiene que funcionar.
        var client = await CreateAuthenticatedClientAsync("st2");

        var url = await UploadAsync(client, Png(), "foto.png", "image/png");

        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = null;

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task ElContentTypeSaleDeLosBytesYNoDeLaExtension()
    {
        // Se sube un GIF con nombre y Content-Type de PNG. Los dos los eligio el cliente;
        // los bytes, no. La respuesta tiene que declarar lo que el archivo realmente es.
        var client = await CreateAuthenticatedClientAsync("st3");

        var url = await UploadAsync(client, Gif(), "mentira.png", "image/png");

        Assert.EndsWith(".gif", url);

        var download = await _factory.CreateClient().GetAsync(url);

        Assert.Equal("image/gif", download.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LaRespuestaLlevaNosniff()
    {
        // La otra mitad de la defensa: sin esta cabecera el navegador puede ignorar el
        // Content-Type y decidir por su cuenta que la respuesta es HTML, que servido desde
        // nuestro dominio seria un XSS con nuestro origen.
        var client = await CreateAuthenticatedClientAsync("st4");

        var url = await UploadAsync(client, Png(), "foto.png", "image/png");
        var download = await _factory.CreateClient().GetAsync(url);

        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task SubirUnaFotoNuevaDejaLaAnteriorInaccesible()
    {
        // Cada subida crea una fila con Id propio y borra la anterior. Que la URL vieja
        // deje de resolver es lo que permite cachear estas respuestas para siempre.
        var client = await CreateAuthenticatedClientAsync("st5");

        var primera = await UploadAsync(client, Png(), "foto.png", "image/png");
        var segunda = await UploadAsync(client, Gif(), "otra.gif", "image/gif");

        Assert.NotEqual(primera, segunda);

        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(primera)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(segunda)).StatusCode);
    }

    [Fact]
    public async Task QuitarElAvatarBorraLaImagen()
    {
        var client = await CreateAuthenticatedClientAsync("st6");

        var url = await UploadAsync(client, Png(), "foto.png", "image/png");

        var removal = await client.DeleteAsync("/api/users/me/avatar");

        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        var profile = await removal.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        Assert.Null(profile!.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/avatars/no-es-un-id.png")]
    [InlineData("/avatars/01a07765a88f777eb3b4a817f5b8c8a9.png")]   // formato valido, no existe
    [InlineData("/avatars/01a07765a88f777eb3b4a817f5b8c8.png")]     // 30 caracteres
    public async Task UnaUrlQueNoCorrespondeDevuelve404(string url)
    {
        // El nombre llega desde la URL. Lo que importa es que ninguna de estas variantes
        // devuelva algo distinto de un 404 limpio.
        var response = await _factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
