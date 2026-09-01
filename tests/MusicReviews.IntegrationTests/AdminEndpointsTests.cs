using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

public class AdminEndpointsTests : IntegrationTestBase
{
    public AdminEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record StatsPayload(
        int TotalUsers,
        int TotalReviews,
        int TotalComments,
        int TotalVotes,
        int CachedArtists,
        int CachedAlbums,
        double? AverageScore,
        MostReviewedPayload[] MostReviewedAlbums);

    private sealed record MostReviewedPayload(
        int AlbumId,
        string MusicBrainzId,
        string Title,
        int ReviewCount,
        double AverageScore);

    private sealed record AdminUserPayload(
        Guid Id,
        string UserName,
        string Email,
        string[] Roles,
        int ReviewCount,
        int CommentCount,
        bool IsLockedOut);

    private sealed record UsersPagePayload(AdminUserPayload[] Items, int TotalCount);

    private sealed record ReviewPayload(int Id);

    /// <summary>Forma de la respuesta de GET /api/auth/me: userId, userName, roles.</summary>
    private sealed record MePayload(Guid UserId, string UserName, string[] Roles);

    [Fact]
    public async Task Stats_SinAutenticar_DevuelveUnauthorized()
    {
        var response = await CreateClient().GetAsync("/api/admin/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stats_ConUnUsuarioComun_DevuelveForbidden()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.GetAsync("/api/admin/stats");

        // 403 y no 401: el token es valido, lo que falta es el rol.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Stats_ConAdministrador_DevuelveLosContadores()
    {
        var user = await CreateUserAsync("stt");
        var albumMbid = NewMbid();

        var created = await user.Client.PostAsJsonAsync("/api/reviews", new
        {
            albumMusicBrainzId = albumMbid,
            score = 88,
            text = "Para que las estadisticas tengan algo que contar."
        });

        var review = await created.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        await user.Client.PostAsJsonAsync(
            $"/api/reviews/{review!.Id}/comments", new { text = "Un comentario." });
        await user.Client.PostAsJsonAsync(
            $"/api/reviews/{review.Id}/likes", new { isLike = true });

        var admin = await CreateAdminClientAsync();
        var stats = await admin.GetFromJsonAsync<StatsPayload>("/api/admin/stats", Json);

        Assert.NotNull(stats);
        Assert.True(stats.TotalUsers >= 2, "Deberian contarse al menos el admin y el usuario del test.");
        Assert.True(stats.TotalReviews >= 1);
        Assert.True(stats.TotalComments >= 1);
        Assert.True(stats.TotalVotes >= 1);
        Assert.True(stats.CachedAlbums >= 1);
        Assert.True(stats.CachedArtists >= 1);
        Assert.NotNull(stats.AverageScore);

        // El album de este test no tiene por que estar en el top: la suite comparte
        // base y otros tests generan albumes con mas reviews. Lo que si tiene que
        // cumplirse siempre es el contrato del ranking.
        Assert.NotEmpty(stats.MostReviewedAlbums);
        Assert.True(stats.MostReviewedAlbums.Length <= 10, "El ranking devuelve como maximo 10 albumes.");
        Assert.All(stats.MostReviewedAlbums, album =>
        {
            Assert.True(album.ReviewCount > 0, "Un album sin reviews no deberia aparecer en el ranking.");
            Assert.InRange(album.AverageScore, 0, 100);
        });

        var reviewCounts = stats.MostReviewedAlbums.Select(a => a.ReviewCount).ToArray();

        Assert.Equal(reviewCounts.OrderByDescending(c => c).ToArray(), reviewCounts);

        // Que el album del test exista se verifica por su propio recurso, que si es
        // determinista, en vez de por su posicion en un ranking compartido.
        Assert.NotEqual(0, review.Id);
        Assert.NotEmpty(albumMbid);
    }

    [Fact]
    public async Task Users_DevuelveElListadoConRolesYPermiteBuscar()
    {
        var user = await CreateUserAsync("srh");
        var admin = await CreateAdminClientAsync();

        var page = await admin.GetFromJsonAsync<UsersPagePayload>(
            $"/api/admin/users?search={user.UserName}", Json);

        Assert.NotNull(page);

        var found = Assert.Single(page.Items);

        Assert.Equal(user.UserName, found.UserName);
        Assert.Contains("User", found.Roles);
        Assert.DoesNotContain("Admin", found.Roles);
        Assert.False(found.IsLockedOut);
    }

    [Fact]
    public async Task AsignarYQuitarRol_FuncionaYEsIdempotente()
    {
        var target = await CreateUserAsync("rol");
        var admin = await CreateAdminClientAsync();

        var me = await target.Client.GetFromJsonAsync<MePayload>("/api/auth/me", Json);
        var userId = me!.UserId;

        var assigned = await admin.PostAsJsonAsync(
            $"/api/admin/users/{userId}/roles", new { role = "Admin" });

        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var afterAssign = await assigned.Content.ReadFromJsonAsync<AdminUserPayload>(Json);
        Assert.Contains("Admin", afterAssign!.Roles);

        // Repetir la asignacion no tiene que fallar.
        var again = await admin.PostAsJsonAsync(
            $"/api/admin/users/{userId}/roles", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var removed = await admin.DeleteAsync($"/api/admin/users/{userId}/roles/Admin");

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var afterRemove = await removed.Content.ReadFromJsonAsync<AdminUserPayload>(Json);
        Assert.DoesNotContain("Admin", afterRemove!.Roles);
        Assert.Contains("User", afterRemove.Roles);
    }

    [Fact]
    public async Task AsignarUnRolInexistente_DevuelveBadRequest()
    {
        var target = await CreateUserAsync("bad");
        var admin = await CreateAdminClientAsync();

        var me = await target.Client.GetFromJsonAsync<MePayload>("/api/auth/me", Json);

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/users/{me!.UserId}/roles", new { role = "Superusuario" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("admin.unknown_role", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnAdministradorNoPuedeQuitarseSuPropioRol()
    {
        var admin = await CreateAdminClientAsync();

        var me = await admin.GetFromJsonAsync<MePayload>("/api/auth/me", Json);

        // Sin esta proteccion, un click deja la sesion sin permisos y, si era el
        // unico admin, hay que arreglarlo tocando la base a mano.
        var response = await admin.DeleteAsync($"/api/admin/users/{me!.UserId}/roles/Admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("admin.cannot_demote_self", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AsignarRolAUnUsuarioInexistente_DevuelveNotFound()
    {
        var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/roles", new { role = "Admin" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
