using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

public class AuthEndpointsTests : IntegrationTestBase
{
    public AuthEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Register_ConDatosValidos_DevuelveTokensYAsignaElRolUser()
    {
        var client = CreateClient();
        var userName = $"reg{Guid.NewGuid():N}"[..16];

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email = $"{userName}@musicreviews.tests",
            password = "TestUser1234"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponsePayload>(Json);

        Assert.NotNull(auth);
        Assert.NotEmpty(auth.AccessToken);
        Assert.NotEmpty(auth.RefreshToken);
        Assert.Equal(userName, auth.User.UserName);
        Assert.Contains("User", auth.User.Roles);
    }

    [Fact]
    public async Task Register_ConEmailRepetido_DevuelveConflict()
    {
        var existing = await CreateUserAsync();

        var response = await CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            userName = $"dup{Guid.NewGuid():N}"[..16],
            email = existing.Email,
            password = "TestUser1234"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("auth.email_taken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_ConPayloadInvalido_DevuelveValidationProblem()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            userName = "x",
            email = "no-es-un-email",
            password = "corta"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("errors", body);
    }

    [Fact]
    public async Task Login_ConContraseniaIncorrecta_DevuelveUnauthorizedSinRevelarSiElUsuarioExiste()
    {
        var user = await CreateUserAsync();

        var wrongPassword = await CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = user.Email,
            password = "OtraCosa1234"
        });

        var unknownUser = await CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "no-existe@musicreviews.tests",
            password = "OtraCosa1234"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);

        // Mismo codigo de error en los dos casos: distinguirlos permitiria enumerar
        // que cuentas existen.
        Assert.Contains("auth.invalid_credentials", await wrongPassword.Content.ReadAsStringAsync());
        Assert.Contains("auth.invalid_credentials", await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_PorNombreDeUsuarioYPorEmail_FuncionanIgual()
    {
        var user = await CreateUserAsync();

        var byEmail = await CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = user.Email,
            password = user.Password
        });

        var byUserName = await CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = user.UserName,
            password = user.Password
        });

        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byUserName.StatusCode);
    }

    [Fact]
    public async Task Me_SinToken_DevuelveUnauthorized()
    {
        var response = await CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ConToken_DevuelveLaIdentidadDelUsuario()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(user.UserName, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_RotaElToken_YElAnteriorDejaDeServir()
    {
        var user = await CreateUserAsync();
        var client = CreateClient();

        var rotated = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = user.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        var newAuth = await rotated.Content.ReadFromJsonAsync<AuthResponsePayload>(Json);

        Assert.NotNull(newAuth);
        Assert.NotEqual(user.RefreshToken, newAuth.RefreshToken);

        // Reusar el token viejo es senial de robo: se corta la familia entera.
        var reused = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = user.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        // Y por eso el token rotado tampoco sirve mas.
        var afterFamilyRevoke = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = newAuth.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, afterFamilyRevoke.StatusCode);
    }

    [Fact]
    public async Task Logout_RevocaElRefreshToken_YEsIdempotente()
    {
        var user = await CreateUserAsync();
        var client = CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = user.RefreshToken });
        var second = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = user.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
