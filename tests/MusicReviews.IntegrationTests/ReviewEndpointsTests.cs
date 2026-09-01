using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

public class ReviewEndpointsTests : IntegrationTestBase
{
    public ReviewEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record ReviewPayload(
        int Id,
        int Score,
        string Text,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt,
        AuthorPayload Author,
        AlbumPayload Album,
        int LikeCount,
        int DislikeCount,
        int CommentCount,
        bool? CurrentUserVote);

    private sealed record AuthorPayload(Guid Id, string UserName, string? AvatarUrl);

    private sealed record AlbumPayload(int Id, string MusicBrainzId, string Title);

    private sealed record PagePayload(ReviewPayload[] Items, int TotalCount);

    private static object NewReview(string albumMbid, int score = 80, string? text = null) => new
    {
        albumMusicBrainzId = albumMbid,
        score,
        text = text ?? "Texto de prueba de la review."
    };

    [Fact]
    public async Task Create_PersisteLaReview_YApareceEnElListadoDelAlbum()
    {
        var user = await CreateUserAsync();
        var albumMbid = NewMbid();

        var created = await user.Client.PostAsJsonAsync("/api/reviews", NewReview(albumMbid, 91));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var review = await created.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        Assert.NotNull(review);
        Assert.Equal(91, review.Score);
        Assert.Equal(user.UserName, review.Author.UserName);
        Assert.Equal(albumMbid, review.Album.MusicBrainzId);

        // Location apuntando al recurso creado, como corresponde a un 201.
        Assert.NotNull(created.Headers.Location);

        var listing = await CreateClient().GetFromJsonAsync<PagePayload>(
            $"/api/albums/{albumMbid}/reviews", Json);

        Assert.NotNull(listing);
        Assert.Equal(1, listing.TotalCount);
        Assert.Equal(review.Id, listing.Items[0].Id);
    }

    [Fact]
    public async Task Create_SinAutenticar_DevuelveUnauthorized()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/reviews", NewReview(NewMbid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_DosVecesSobreElMismoAlbum_DevuelveConflict()
    {
        var user = await CreateUserAsync();
        var albumMbid = NewMbid();

        var first = await user.Client.PostAsJsonAsync("/api/reviews", NewReview(albumMbid));
        var second = await user.Client.PostAsJsonAsync("/api/reviews", NewReview(albumMbid, 50));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // La regla la sostiene el indice unico (UserId, AlbumId) en Postgres,
        // no un chequeo previo en memoria.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("reviews.already_exists", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_DosUsuariosDistintosSobreElMismoAlbum_FuncionaParaLosDos()
    {
        var albumMbid = NewMbid();
        var first = await CreateUserAsync("a");
        var second = await CreateUserAsync("b");

        var one = await first.Client.PostAsJsonAsync("/api/reviews", NewReview(albumMbid, 70));
        var two = await second.Client.PostAsJsonAsync("/api/reviews", NewReview(albumMbid, 30));

        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);

        var listing = await CreateClient().GetFromJsonAsync<PagePayload>(
            $"/api/albums/{albumMbid}/reviews", Json);

        Assert.Equal(2, listing!.TotalCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Create_ConPuntajeFueraDeRango_DevuelveBadRequest(int score)
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/reviews", NewReview(NewMbid(), score));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_SobreUnAlbumInexistente_DevuelveNotFound()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/reviews", NewReview(FakeMusicCatalogService.UnknownMusicBrainzId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_PorQuienNoEsElAutor_DevuelveForbidden()
    {
        var author = await CreateUserAsync("author");
        var stranger = await CreateUserAsync("stranger");

        var created = await author.Client.PostAsJsonAsync("/api/reviews", NewReview(NewMbid()));
        var review = await created.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        var response = await stranger.Client.PutAsJsonAsync(
            $"/api/reviews/{review!.Id}", new { score = 10, text = "Reescribiendo lo ajeno." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_PorElAutor_ActualizaYSellaUpdatedAt()
    {
        var author = await CreateUserAsync();

        var created = await author.Client.PostAsJsonAsync("/api/reviews", NewReview(NewMbid(), 60));
        var review = await created.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        Assert.Null(review!.UpdatedAt);

        var response = await author.Client.PutAsJsonAsync(
            $"/api/reviews/{review.Id}", new { score = 95, text = "Lo escuche de nuevo." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        Assert.Equal(95, updated!.Score);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Delete_PorUnAdministrador_FuncionaAunqueNoSeaElAutor()
    {
        var author = await CreateUserAsync();
        var admin = await CreateAdminClientAsync();

        var created = await author.Client.PostAsJsonAsync("/api/reviews", NewReview(NewMbid()));
        var review = await created.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        // Es la moderacion: el admin borra contenido de cualquiera, pero no lo edita.
        var deleted = await admin.DeleteAsync($"/api/reviews/{review!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var afterDelete = await CreateClient().GetAsync($"/api/reviews/{review.Id}");

        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Listado_TraeLosContadoresDeVotosYComentariosResueltos()
    {
        var author = await CreateUserAsync("cnt");
        var voter = await CreateUserAsync("vot");
        var albumMbid = NewMbid();

        var created = await author.Client.PostAsJsonAsync("/api/reviews", NewReview(albumMbid));
        var review = await created.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        await voter.Client.PostAsJsonAsync($"/api/reviews/{review!.Id}/likes", new { isLike = true });
        await author.Client.PostAsJsonAsync($"/api/reviews/{review.Id}/likes", new { isLike = false });
        await voter.Client.PostAsJsonAsync(
            $"/api/reviews/{review.Id}/comments", new { text = "Buen punto." });

        var listing = await author.Client.GetFromJsonAsync<PagePayload>(
            $"/api/albums/{albumMbid}/reviews", Json);

        var listed = Assert.Single(listing!.Items);

        Assert.Equal(1, listed.LikeCount);
        Assert.Equal(1, listed.DislikeCount);
        Assert.Equal(1, listed.CommentCount);

        // El autor voto en contra: el DTO tiene que reflejar SU voto, no uno generico.
        Assert.False(listed.CurrentUserVote);
    }

    [Fact]
    public async Task Listado_DeUnUsuarioInexistente_DevuelveNotFound()
    {
        var response = await CreateClient().GetAsync($"/api/users/no-existe-{Guid.NewGuid():N}/reviews");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
