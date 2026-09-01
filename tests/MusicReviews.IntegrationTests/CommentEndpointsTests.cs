using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// Verifica el hilo anidado de punta a punta: contra Postgres real y a traves de HTTP.
/// Los tests unitarios de <c>CommentTreeBuilder</c> cubren el algoritmo; estos cubren
/// que la consulta, la proyeccion y la serializacion produzcan el arbol correcto.
/// </summary>
public class CommentEndpointsTests : IntegrationTestBase
{
    public CommentEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record CommentPayload(
        int Id,
        int? ParentCommentId,
        int Depth,
        string? Text,
        bool IsDeleted,
        AuthorPayload? Author,
        int LikeCount,
        int DislikeCount,
        CommentPayload[] Replies);

    private sealed record AuthorPayload(Guid Id, string UserName, string? AvatarUrl);

    private sealed record ReviewPayload(int Id, int CommentCount);

    private async Task<int> CreateReviewAsync(TestUser user)
    {
        var response = await user.Client.PostAsJsonAsync("/api/reviews", new
        {
            albumMusicBrainzId = NewMbid(),
            score = 75,
            text = "Review de soporte para el hilo."
        });

        response.EnsureSuccessStatusCode();

        var review = await response.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        return review!.Id;
    }

    private static async Task<CommentPayload> PostCommentAsync(
        HttpClient client, int reviewId, string text, int? parentId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/comments", new { text, parentCommentId = parentId });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CommentPayload>(Json))!;
    }

    [Fact]
    public async Task Hilo_DeTresNiveles_SeDevuelveAnidadoYConLasProfundidadesCorrectas()
    {
        var author = await CreateUserAsync("thr");
        var other = await CreateUserAsync("oth");
        var reviewId = await CreateReviewAsync(author);

        var root = await PostCommentAsync(author.Client, reviewId, "Primer nivel.");
        var reply = await PostCommentAsync(other.Client, reviewId, "Segundo nivel.", root.Id);
        var deep = await PostCommentAsync(author.Client, reviewId, "Tercer nivel.", reply.Id);

        var tree = await CreateClient().GetFromJsonAsync<CommentPayload[]>(
            $"/api/reviews/{reviewId}/comments", Json);

        var single = Assert.Single(tree!);

        Assert.Equal(root.Id, single.Id);
        Assert.Equal(0, single.Depth);

        var level2 = Assert.Single(single.Replies);
        Assert.Equal(reply.Id, level2.Id);
        Assert.Equal(1, level2.Depth);

        var level3 = Assert.Single(level2.Replies);
        Assert.Equal(deep.Id, level3.Id);
        Assert.Equal(2, level3.Depth);
        Assert.Empty(level3.Replies);
    }

    [Fact]
    public async Task Hilo_ConVariasRamas_MantieneElOrdenCronologicoDeLasRespuestas()
    {
        var user = await CreateUserAsync("brn");
        var reviewId = await CreateReviewAsync(user);

        var root = await PostCommentAsync(user.Client, reviewId, "Raiz.");
        var first = await PostCommentAsync(user.Client, reviewId, "Primera respuesta.", root.Id);
        var second = await PostCommentAsync(user.Client, reviewId, "Segunda respuesta.", root.Id);
        var third = await PostCommentAsync(user.Client, reviewId, "Tercera respuesta.", root.Id);

        var tree = await CreateClient().GetFromJsonAsync<CommentPayload[]>(
            $"/api/reviews/{reviewId}/comments", Json);

        var replies = Assert.Single(tree!).Replies;

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, replies.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task BorrarUnNodoIntermedio_NoRompeElHilo()
    {
        var author = await CreateUserAsync("del");
        var other = await CreateUserAsync("kid");
        var reviewId = await CreateReviewAsync(author);

        var root = await PostCommentAsync(author.Client, reviewId, "Se va a borrar.");
        var reply = await PostCommentAsync(other.Client, reviewId, "Respuesta de un tercero.", root.Id);

        var deleted = await author.Client.DeleteAsync($"/api/comments/{root.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var tree = await CreateClient().GetFromJsonAsync<CommentPayload[]>(
            $"/api/reviews/{reviewId}/comments", Json);

        var node = Assert.Single(tree!);

        // El nodo sobrevive para sostener la respuesta, pero sin contenido ni autor.
        Assert.True(node.IsDeleted);
        Assert.Null(node.Text);
        Assert.Null(node.Author);

        var survivor = Assert.Single(node.Replies);
        Assert.Equal(reply.Id, survivor.Id);
        Assert.False(survivor.IsDeleted);
    }

    [Fact]
    public async Task Responder_AUnComentarioBorrado_DevuelveBadRequest()
    {
        var user = await CreateUserAsync("rdl");
        var reviewId = await CreateReviewAsync(user);

        var comment = await PostCommentAsync(user.Client, reviewId, "Adios.");
        await user.Client.DeleteAsync($"/api/comments/{comment.Id}");

        var response = await user.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/comments",
            new { text = "Respondiendo a la nada.", parentCommentId = comment.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Responder_AUnComentarioDeOtraReview_DevuelveBadRequest()
    {
        var user = await CreateUserAsync("xrv");
        var reviewA = await CreateReviewAsync(user);
        var reviewB = await CreateReviewAsync(user);

        var commentInA = await PostCommentAsync(user.Client, reviewA, "Vivo en la review A.");

        // Sin esta validacion el nodo quedaria huerfano: el hilo se trae por ReviewId
        // y su padre nunca vendria en el mismo conjunto.
        var response = await user.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewB}/comments",
            new { text = "Colgado de otra review.", parentCommentId = commentInA.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("comments.parent_other_review", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anidar_MasAlaDelLimite_DevuelveBadRequest()
    {
        var user = await CreateUserAsync("dep");
        var reviewId = await CreateReviewAsync(user);

        var current = await PostCommentAsync(user.Client, reviewId, "Nivel 0.");
        var reachedLimit = false;

        for (var level = 1; level <= 20; level++)
        {
            var response = await user.Client.PostAsJsonAsync(
                $"/api/reviews/{reviewId}/comments",
                new { text = $"Nivel {level}.", parentCommentId = current.Id });

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                Assert.Contains("comments.max_depth", await response.Content.ReadAsStringAsync());
                reachedLimit = true;
                break;
            }

            response.EnsureSuccessStatusCode();
            current = (await response.Content.ReadFromJsonAsync<CommentPayload>(Json))!;
        }

        Assert.True(reachedLimit, "El limite de profundidad nunca se aplico.");
    }

    [Fact]
    public async Task Editar_UnComentarioAjeno_DevuelveForbidden()
    {
        var author = await CreateUserAsync("ca");
        var stranger = await CreateUserAsync("cs");
        var reviewId = await CreateReviewAsync(author);

        var comment = await PostCommentAsync(author.Client, reviewId, "Mio.");

        var response = await stranger.Client.PutAsJsonAsync(
            $"/api/comments/{comment.Id}", new { text = "Ajeno." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Hilo_DeUnaReviewInexistente_DevuelveNotFound()
    {
        var response = await CreateClient().GetAsync("/api/reviews/999999/comments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ComentariosBorrados_NoCuentanEnElContadorDeLaReview()
    {
        var user = await CreateUserAsync("cc");
        var reviewId = await CreateReviewAsync(user);

        var keep = await PostCommentAsync(user.Client, reviewId, "Se queda.");
        var remove = await PostCommentAsync(user.Client, reviewId, "Se borra.");

        await user.Client.DeleteAsync($"/api/comments/{remove.Id}");

        var review = await CreateClient().GetFromJsonAsync<ReviewPayload>(
            $"/api/reviews/{reviewId}", Json);

        Assert.Equal(1, review!.CommentCount);
        Assert.NotEqual(0, keep.Id);
    }
}
