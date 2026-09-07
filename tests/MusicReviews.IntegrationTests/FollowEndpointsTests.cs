using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

public class FollowEndpointsTests : IntegrationTestBase
{
    public FollowEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record ProfilePayload(
        Guid Id,
        string UserName,
        int FollowerCount,
        int FollowingCount,
        bool IsFollowedByCurrentUser,
        bool IsCurrentUser);

    private sealed record FollowUserPayload(
        Guid Id,
        string UserName,
        int ReviewCount,
        DateTimeOffset FollowedAt,
        bool IsFollowedByCurrentUser);

    private sealed record FollowPagePayload(FollowUserPayload[] Items, int TotalCount);

    [Fact]
    public async Task Follow_SinAutenticar_DevuelveUnauthorized()
    {
        var target = await CreateUserAsync("tgt");

        var response = await CreateClient().PostAsync($"/api/users/{target.UserName}/follow", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Follow_ActualizaLosContadoresDeLasDosPartes()
    {
        var follower = await CreateUserAsync("fwr");
        var target = await CreateUserAsync("tgt");

        var response = await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // La respuesta es el perfil del seguido, ya actualizado: el cliente no necesita
        // pedirlo de nuevo para repintar el boton y el contador.
        var profile = await response.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        Assert.Equal(target.UserName, profile!.UserName);
        Assert.Equal(1, profile.FollowerCount);
        Assert.True(profile.IsFollowedByCurrentUser);
        Assert.False(profile.IsCurrentUser);

        var followerProfile = await follower.Client.GetFromJsonAsync<ProfilePayload>(
            $"/api/users/{follower.UserName}", Json);

        Assert.Equal(1, followerProfile!.FollowingCount);
        Assert.Equal(0, followerProfile.FollowerCount);
        Assert.True(followerProfile.IsCurrentUser);
    }

    [Fact]
    public async Task Follow_DosVeces_EsIdempotenteYNoDuplicaElContador()
    {
        var follower = await CreateUserAsync("dup");
        var target = await CreateUserAsync("tgt");

        await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);
        var second = await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var profile = await second.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        // La PK compuesta (FollowerId, FollowedId) es la que lo garantiza, no un
        // chequeo previo que un doble click podria esquivar.
        Assert.Equal(1, profile!.FollowerCount);
    }

    [Fact]
    public async Task Follow_ASiMismo_DevuelveBadRequest()
    {
        var user = await CreateUserAsync("self");

        var response = await user.Client.PostAsync($"/api/users/{user.UserName}/follow", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("users.cannot_follow_self", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Follow_AUnUsuarioInexistente_DevuelveNotFound()
    {
        var user = await CreateUserAsync("nf");

        var response = await user.Client.PostAsync(
            $"/api/users/no-existe-{Guid.NewGuid():N}/follow", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unfollow_QuitaLaRelacion_YEsIdempotente()
    {
        var follower = await CreateUserAsync("unf");
        var target = await CreateUserAsync("tgt");

        await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);

        var first = await follower.Client.DeleteAsync($"/api/users/{target.UserName}/follow");
        var second = await follower.Client.DeleteAsync($"/api/users/{target.UserName}/follow");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var profile = await second.Content.ReadFromJsonAsync<ProfilePayload>(Json);

        Assert.Equal(0, profile!.FollowerCount);
        Assert.False(profile.IsFollowedByCurrentUser);
    }

    [Fact]
    public async Task Followers_YFollowing_DevuelvenCadaLadoDeLaRelacion()
    {
        var alice = await CreateUserAsync("alice");
        var bob = await CreateUserAsync("bob");

        await alice.Client.PostAsync($"/api/users/{bob.UserName}/follow", null);

        var bobFollowers = await CreateClient().GetFromJsonAsync<FollowPagePayload>(
            $"/api/users/{bob.UserName}/followers", Json);

        var aliceFollowing = await CreateClient().GetFromJsonAsync<FollowPagePayload>(
            $"/api/users/{alice.UserName}/following", Json);

        Assert.Equal(1, bobFollowers!.TotalCount);
        Assert.Equal(alice.UserName, Assert.Single(bobFollowers.Items).UserName);

        Assert.Equal(1, aliceFollowing!.TotalCount);
        Assert.Equal(bob.UserName, Assert.Single(aliceFollowing.Items).UserName);

        // La relacion es dirigida: que alice siga a bob no implica lo contrario.
        var aliceFollowers = await CreateClient().GetFromJsonAsync<FollowPagePayload>(
            $"/api/users/{alice.UserName}/followers", Json);

        Assert.Equal(0, aliceFollowers!.TotalCount);
    }

    [Fact]
    public async Task Followers_MarcaAQuienesElUsuarioActualTambienSigue()
    {
        var alice = await CreateUserAsync("mutu");
        var bob = await CreateUserAsync("bob");
        var carol = await CreateUserAsync("carol");

        // Alice y carol siguen a bob; alice ademas sigue a carol.
        await alice.Client.PostAsync($"/api/users/{bob.UserName}/follow", null);
        await carol.Client.PostAsync($"/api/users/{bob.UserName}/follow", null);
        await alice.Client.PostAsync($"/api/users/{carol.UserName}/follow", null);

        var followers = await alice.Client.GetFromJsonAsync<FollowPagePayload>(
            $"/api/users/{bob.UserName}/followers", Json);

        var carolRow = followers!.Items.Single(f => f.UserName == carol.UserName);
        var aliceRow = followers.Items.Single(f => f.UserName == alice.UserName);

        // Se resuelve en la misma consulta que trae el listado, no con una query por fila.
        Assert.True(carolRow.IsFollowedByCurrentUser);
        Assert.False(aliceRow.IsFollowedByCurrentUser);
    }

    [Fact]
    public async Task Followers_DeUnUsuarioInexistente_DevuelveNotFound()
    {
        var response = await CreateClient().GetAsync(
            $"/api/users/no-existe-{Guid.NewGuid():N}/followers");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Perfil_Anonimo_NoMarcaSeguimientoNiPropiedad()
    {
        var target = await CreateUserAsync("anon");
        var follower = await CreateUserAsync("fwr");

        await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);

        var profile = await CreateClient().GetFromJsonAsync<ProfilePayload>(
            $"/api/users/{target.UserName}", Json);

        Assert.Equal(1, profile!.FollowerCount);
        Assert.False(profile.IsFollowedByCurrentUser);
        Assert.False(profile.IsCurrentUser);
    }
}
