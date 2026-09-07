using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

public class ActivityEndpointsTests : IntegrationTestBase
{
    public ActivityEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record ActorPayload(Guid Id, string UserName);

    private sealed record ActivityAlbumPayload(int Id, string MusicBrainzId, string Title);

    private sealed record ActivityEntryPayload(
        string Id,
        int Kind,
        DateTimeOffset CreatedAt,
        ActorPayload Actor,
        ActivityAlbumPayload? Album,
        string? Excerpt,
        bool? IsLike);

    private sealed record FeedPayload(ActivityEntryPayload[] Items, string? NextCursor, bool HasMore);

    private sealed record ReviewPayload(int Id);

    private const int ReviewPublished = 1;
    private const int CommentPublished = 2;
    private const int ReviewVoted = 3;

    private async Task<int> PublishReviewAsync(TestUser user, int score = 80)
    {
        var response = await user.Client.PostAsJsonAsync("/api/reviews", new
        {
            albumMusicBrainzId = NewMbid(),
            score,
            text = "Texto de la reseña para el timeline."
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ReviewPayload>(Json))!.Id;
    }

    [Fact]
    public async Task Actividad_DeUnUsuario_IncluyeReseniasComentariosYVotos()
    {
        var author = await CreateUserAsync("act");
        var other = await CreateUserAsync("oth");

        var reviewId = await PublishReviewAsync(author);

        // El otro usuario publica su propia reseña para que author pueda comentarla
        // y votarla sin votarse a si mismo.
        var otherReviewId = await PublishReviewAsync(other);

        await author.Client.PostAsJsonAsync(
            $"/api/reviews/{otherReviewId}/comments", new { text = "Buen punto." });
        await author.Client.PostAsJsonAsync(
            $"/api/reviews/{otherReviewId}/likes", new { isLike = true });

        var feed = await CreateClient().GetFromJsonAsync<FeedPayload>(
            $"/api/users/{author.UserName}/activity", Json);

        Assert.NotNull(feed);

        var kinds = feed.Items.Select(i => i.Kind).ToArray();

        Assert.Contains(ReviewPublished, kinds);
        Assert.Contains(CommentPublished, kinds);
        Assert.Contains(ReviewVoted, kinds);

        // Toda entrada tiene actor y album: son las dos cosas que el feed necesita
        // para renderizar una fila.
        Assert.All(feed.Items, entry =>
        {
            Assert.Equal(author.UserName, entry.Actor.UserName);
            Assert.NotNull(entry.Album);
        });

        var vote = feed.Items.Single(i => i.Kind == ReviewVoted);
        Assert.True(vote.IsLike);
        Assert.Null(vote.Excerpt);

        var published = feed.Items.Single(i => i.Kind == ReviewPublished);
        Assert.Equal($"review:{reviewId}", published.Id);
        Assert.NotNull(published.Excerpt);
    }

    [Fact]
    public async Task Actividad_VieneOrdenadaDeMasRecienteAMasAntigua()
    {
        var user = await CreateUserAsync("ord");

        await PublishReviewAsync(user);
        await PublishReviewAsync(user);
        await PublishReviewAsync(user);

        var feed = await CreateClient().GetFromJsonAsync<FeedPayload>(
            $"/api/users/{user.UserName}/activity", Json);

        var dates = feed!.Items.Select(i => i.CreatedAt).ToArray();

        Assert.Equal(dates.OrderByDescending(d => d).ToArray(), dates);
    }

    [Fact]
    public async Task Actividad_DeUnUsuarioInexistente_DevuelveNotFound()
    {
        var response = await CreateClient().GetAsync(
            $"/api/users/no-existe-{Guid.NewGuid():N}/activity");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Feed_SinAutenticar_DevuelveUnauthorized()
    {
        var response = await CreateClient().GetAsync("/api/feed");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Feed_SinSeguirANadie_DevuelveVacio()
    {
        var user = await CreateUserAsync("solo");

        var feed = await user.Client.GetFromJsonAsync<FeedPayload>("/api/feed", Json);

        Assert.Empty(feed!.Items);
        Assert.False(feed.HasMore);
        Assert.Null(feed.NextCursor);
    }

    [Fact]
    public async Task Feed_TraeLaActividadDeLosSeguidos_YNoLaPropia()
    {
        var follower = await CreateUserAsync("fwr");
        var followed = await CreateUserAsync("fwd");

        await PublishReviewAsync(followed);
        await PublishReviewAsync(follower);

        await follower.Client.PostAsync($"/api/users/{followed.UserName}/follow", null);

        var feed = await follower.Client.GetFromJsonAsync<FeedPayload>("/api/feed", Json);

        Assert.NotEmpty(feed!.Items);

        // El feed es de a quienes seguis: la propia actividad se ve en el perfil.
        Assert.All(feed.Items, entry =>
            Assert.Equal(followed.UserName, entry.Actor.UserName));
    }

    [Fact]
    public async Task Feed_DejarDeSeguir_SacaLaActividadDelFeed()
    {
        var follower = await CreateUserAsync("unf");
        var followed = await CreateUserAsync("fwd");

        await PublishReviewAsync(followed);
        await follower.Client.PostAsync($"/api/users/{followed.UserName}/follow", null);

        var before = await follower.Client.GetFromJsonAsync<FeedPayload>("/api/feed", Json);
        Assert.NotEmpty(before!.Items);

        await follower.Client.DeleteAsync($"/api/users/{followed.UserName}/follow");

        var after = await follower.Client.GetFromJsonAsync<FeedPayload>("/api/feed", Json);
        Assert.Empty(after!.Items);
    }

    [Fact]
    public async Task Actividad_PaginadaPorCursor_NoRepiteNiPierdeEntradas()
    {
        var user = await CreateUserAsync("pag");

        // Seis reseñas mas los votos que generan: suficiente para tres paginas de dos.
        for (var i = 0; i < 6; i++)
        {
            await PublishReviewAsync(user, 60 + i);
        }

        var seen = new List<string>();
        string? cursor = null;

        for (var page = 0; page < 10; page++)
        {
            var url = $"/api/users/{user.UserName}/activity?limit=2"
                      + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

            var result = await CreateClient().GetFromJsonAsync<FeedPayload>(url, Json);

            if (result!.Items.Length == 0)
            {
                break;
            }

            seen.AddRange(result.Items.Select(i => i.Id));

            if (!result.HasMore)
            {
                break;
            }

            cursor = result.NextCursor;
            Assert.NotNull(cursor);
        }

        Assert.Equal(6, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task Actividad_ConUnCursorInvalido_EmpiezaDesdeElPrincipioEnVezDeFallar()
    {
        var user = await CreateUserAsync("bad");
        await PublishReviewAsync(user);

        var response = await CreateClient().GetAsync(
            $"/api/users/{user.UserName}/activity?cursor=esto-no-es-un-cursor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var feed = await response.Content.ReadFromJsonAsync<FeedPayload>(Json);

        Assert.NotEmpty(feed!.Items);
    }

    [Fact]
    public async Task Actividad_NoIncluyeComentariosBorrados()
    {
        var author = await CreateUserAsync("del");
        var other = await CreateUserAsync("oth");

        var reviewId = await PublishReviewAsync(other);

        var comment = await author.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/comments", new { text = "Se borra." });

        var commentId = (await comment.Content.ReadFromJsonAsync<ReviewPayload>(Json))!.Id;

        await author.Client.DeleteAsync($"/api/comments/{commentId}");

        var feed = await CreateClient().GetFromJsonAsync<FeedPayload>(
            $"/api/users/{author.UserName}/activity", Json);

        // Derivar la actividad de las tablas reales tiene esta ventaja: el borrado
        // logico se refleja solo, sin que haya que sincronizar una tabla de feed.
        Assert.DoesNotContain(feed!.Items, i => i.Id == $"comment:{commentId}");
    }
}
