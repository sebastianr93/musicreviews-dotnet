using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicReviews.IntegrationTests.Infrastructure;

/// <summary>Base con los ayudantes que casi todos los tests necesitan.</summary>
[Collection(ApiTestCollection.Name)]
public abstract class IntegrationTestBase
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected IntegrationTestBase(MusicReviewsApiFactory factory)
    {
        Factory = factory;
    }

    protected MusicReviewsApiFactory Factory { get; }

    protected HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>
    /// Registra un usuario nuevo y devuelve un cliente ya autenticado.
    /// Cada test crea el suyo: como comparten base, datos unicos por test es lo que
    /// evita que el orden de ejecucion cambie el resultado.
    /// </summary>
    protected async Task<TestUser> CreateUserAsync(string prefix = "user")
    {
        var client = CreateClient();
        var userName = $"{prefix}{Guid.NewGuid():N}"[..20];
        var email = $"{userName}@musicreviews.tests";
        const string password = "TestUser1234";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email,
            password
        });

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(Json);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return new TestUser(client, userName, email, password, auth.AccessToken, auth.RefreshToken);
    }

    /// <summary>Cliente autenticado como el administrador sembrado al arrancar.</summary>
    protected async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = MusicReviewsApiFactory.AdminEmail,
            password = MusicReviewsApiFactory.AdminPassword
        });

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(Json);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }

    /// <summary>MBID valido y unico por llamada, para no reusar albumes entre tests.</summary>
    protected static string NewMbid() => Guid.NewGuid().ToString();

    protected sealed record TestUser(
        HttpClient Client,
        string UserName,
        string Email,
        string Password,
        string AccessToken,
        string RefreshToken);

    protected sealed record AuthResponsePayload(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        UserSummaryPayload User);

    protected sealed record UserSummaryPayload(
        Guid Id,
        string UserName,
        string Email,
        string? AvatarUrl,
        string[] Roles);
}
