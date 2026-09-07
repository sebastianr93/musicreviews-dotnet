using System.Net;
using System.Net.Http.Json;
using MusicReviews.Domain.Enums;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// Los avisos se generan como efecto de otras acciones, nunca por una llamada directa.
/// Por eso todos estos tests hacen la accion real —comentar, votar, seguir— y despues
/// miran la campana: es la unica forma de verificar que el enganche existe.
/// </summary>
public class NotificationEndpointsTests : IntegrationTestBase
{
    public NotificationEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    // ------------------------------------------------------------------
    // Payloads
    // ------------------------------------------------------------------

    private sealed record NotificationPayload(
        int Id,
        NotificationType Type,
        DateTimeOffset CreatedAt,
        bool IsRead,
        AuthorPayload? Actor,
        int? ReviewId,
        int? CommentId,
        AlbumPayload? Album,
        bool? IsLike);

    private sealed record AuthorPayload(Guid Id, string UserName, string? AvatarUrl);

    private sealed record AlbumPayload(
        string MusicBrainzId, string Title, string? CoverArtUrl, string ArtistName);

    private sealed record NotificationPagePayload(
        NotificationPayload[] Items, string? NextCursor, bool HasMore);

    private sealed record UnreadCountPayload(int Unread);

    private sealed record MarkAllPayload(int MarkedAsRead);

    private sealed record ReviewPayload(int Id);

    private sealed record CommentPayload(int Id, int? ParentCommentId, int Depth);

    // ------------------------------------------------------------------
    // Ayudantes
    // ------------------------------------------------------------------

    private async Task<int> CreateReviewAsync(TestUser user)
    {
        var response = await user.Client.PostAsJsonAsync("/api/reviews", new
        {
            albumMusicBrainzId = NewMbid(),
            score = 80,
            text = "Review de soporte para los avisos."
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ReviewPayload>(Json))!.Id;
    }

    private static async Task<CommentPayload> PostCommentAsync(
        HttpClient client, int reviewId, string text, int? parentId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/comments", new { text, parentCommentId = parentId });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CommentPayload>(Json))!;
    }

    private async Task<NotificationPayload[]> GetNotificationsAsync(
        HttpClient client, bool unreadOnly = false, string? cursor = null, int limit = 20)
    {
        var url = $"/api/notifications?limit={limit}&unreadOnly={unreadOnly}"
                  + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

        var page = await client.GetFromJsonAsync<NotificationPagePayload>(url, Json);

        return page!.Items;
    }

    private async Task<int> GetUnreadCountAsync(HttpClient client)
    {
        var payload = await client.GetFromJsonAsync<UnreadCountPayload>(
            "/api/notifications/unread-count", Json);

        return payload!.Unread;
    }

    // ------------------------------------------------------------------
    // Acceso
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("/api/notifications")]
    [InlineData("/api/notifications/unread-count")]
    public async Task Listado_SinAutenticar_DevuelveUnauthorized(string url)
    {
        var response = await CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_Nuevo_NoTieneAvisos()
    {
        var user = await CreateUserAsync("nnew");

        Assert.Empty(await GetNotificationsAsync(user.Client));
        Assert.Equal(0, await GetUnreadCountAsync(user.Client));
    }

    // ------------------------------------------------------------------
    // Comentarios
    // ------------------------------------------------------------------

    [Fact]
    public async Task Comentar_LaReviewDeOtro_AvisaAlAutor()
    {
        var author = await CreateUserAsync("nau");
        var other = await CreateUserAsync("noth");
        var reviewId = await CreateReviewAsync(author);

        var comment = await PostCommentAsync(other.Client, reviewId, "Buena reseña.");

        var notification = Assert.Single(await GetNotificationsAsync(author.Client));

        Assert.Equal(NotificationType.ReviewCommented, notification.Type);
        Assert.False(notification.IsRead);
        Assert.Equal(other.UserName, notification.Actor!.UserName);
        Assert.Equal(reviewId, notification.ReviewId);
        Assert.Equal(comment.Id, notification.CommentId);
        Assert.NotNull(notification.Album);
        Assert.Equal(1, await GetUnreadCountAsync(author.Client));
    }

    [Fact]
    public async Task Comentar_LaPropiaReview_NoGeneraAviso()
    {
        // Nadie necesita que le avisen de lo que acaba de hacer.
        var author = await CreateUserAsync("nself");
        var reviewId = await CreateReviewAsync(author);

        await PostCommentAsync(author.Client, reviewId, "Me comento a mi mismo.");

        Assert.Empty(await GetNotificationsAsync(author.Client));
    }

    [Fact]
    public async Task Responder_AvisaAlPadre_YNoAlAutorDeLaReview()
    {
        // En un hilo largo, avisarle tambien al autor de la reseña por cada respuesta
        // anidada entre terceros es el ruido que hace que la gente deje de mirar la
        // campana. El aviso va a quien fue interpelado.
        var author = await CreateUserAsync("nrva");
        var commenter = await CreateUserAsync("ncmt");
        var replier = await CreateUserAsync("nrpl");

        var reviewId = await CreateReviewAsync(author);
        var root = await PostCommentAsync(commenter.Client, reviewId, "Primer nivel.");
        var reply = await PostCommentAsync(replier.Client, reviewId, "Te respondo.", root.Id);

        var forCommenter = Assert.Single(await GetNotificationsAsync(commenter.Client));

        Assert.Equal(NotificationType.CommentReplied, forCommenter.Type);
        Assert.Equal(replier.UserName, forCommenter.Actor!.UserName);
        Assert.Equal(reply.Id, forCommenter.CommentId);

        // El autor de la reseña sigue con el unico aviso del comentario raiz.
        var forAuthor = await GetNotificationsAsync(author.Client);

        Assert.Single(forAuthor);
        Assert.Equal(NotificationType.ReviewCommented, forAuthor[0].Type);
        Assert.Equal(root.Id, forAuthor[0].CommentId);
    }

    [Fact]
    public async Task Responderse_ASiMismo_NoGeneraAviso()
    {
        var author = await CreateUserAsync("nsrp");
        var other = await CreateUserAsync("noth");

        var reviewId = await CreateReviewAsync(author);
        var root = await PostCommentAsync(other.Client, reviewId, "Primer nivel.");

        var before = await GetUnreadCountAsync(other.Client);

        await PostCommentAsync(other.Client, reviewId, "Me sigo a mi mismo.", root.Id);

        Assert.Equal(before, await GetUnreadCountAsync(other.Client));
    }

    // ------------------------------------------------------------------
    // Votos
    // ------------------------------------------------------------------

    [Fact]
    public async Task Votar_UnaReviewAjena_AvisaAlAutorConElSentidoDelVoto()
    {
        var author = await CreateUserAsync("nvau");
        var voter = await CreateUserAsync("nvot");
        var reviewId = await CreateReviewAsync(author);

        var response = await voter.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/likes", new { isLike = true });

        response.EnsureSuccessStatusCode();

        var notification = Assert.Single(await GetNotificationsAsync(author.Client));

        Assert.Equal(NotificationType.ReviewVoted, notification.Type);
        Assert.True(notification.IsLike);
        Assert.Equal(reviewId, notification.ReviewId);
        Assert.Null(notification.CommentId);
    }

    [Fact]
    public async Task Cambiar_ElSentidoDelVoto_ActualizaElAvisoEnVezDeSumarOtro()
    {
        var author = await CreateUserAsync("nvch");
        var voter = await CreateUserAsync("nvot");
        var reviewId = await CreateReviewAsync(author);

        await voter.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/likes", new { isLike = true });
        await voter.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/likes", new { isLike = false });

        var notification = Assert.Single(await GetNotificationsAsync(author.Client));

        Assert.False(notification.IsLike);
        Assert.Equal(1, await GetUnreadCountAsync(author.Client));
    }

    [Fact]
    public async Task Retirar_ElVoto_RetiraElAvisoSinLeer()
    {
        // Sin esto, poner y sacar el voto repetidamente llenaria la campana de avisos
        // de algo que ya no es cierto.
        var author = await CreateUserAsync("nvrt");
        var voter = await CreateUserAsync("nvot");
        var reviewId = await CreateReviewAsync(author);

        await voter.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/likes", new { isLike = true });

        Assert.Equal(1, await GetUnreadCountAsync(author.Client));

        // Mismo voto: es un toggle, lo retira.
        await voter.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/likes", new { isLike = true });

        Assert.Equal(0, await GetUnreadCountAsync(author.Client));
        Assert.Empty(await GetNotificationsAsync(author.Client));
    }

    [Fact]
    public async Task Retirar_ElVoto_NoBorraElAvisoYaLeido()
    {
        // Borrar lo que el usuario ya vio seria reescribirle el historial.
        var author = await CreateUserAsync("nvrd");
        var voter = await CreateUserAsync("nvot");
        var reviewId = await CreateReviewAsync(author);

        await voter.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/likes", new { isLike = true });

        var notification = Assert.Single(await GetNotificationsAsync(author.Client));

        var read = await author.Client.PostAsync($"/api/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        await voter.Client.DeleteAsync($"/api/reviews/{reviewId}/likes");

        var remaining = Assert.Single(await GetNotificationsAsync(author.Client));

        Assert.True(remaining.IsRead);
        Assert.Equal(0, await GetUnreadCountAsync(author.Client));
    }

    [Fact]
    public async Task Votar_UnComentarioAjeno_AvisaAlAutorDelComentario()
    {
        var author = await CreateUserAsync("ncva");
        var commenter = await CreateUserAsync("ncvc");
        var voter = await CreateUserAsync("ncvv");

        var reviewId = await CreateReviewAsync(author);
        var comment = await PostCommentAsync(commenter.Client, reviewId, "Comentario votable.");

        await voter.Client.PostAsJsonAsync(
            $"/api/comments/{comment.Id}/likes", new { isLike = true });

        var notifications = await GetNotificationsAsync(commenter.Client);
        var vote = notifications.Single(n => n.Type == NotificationType.CommentVoted);

        Assert.Equal(comment.Id, vote.CommentId);
        Assert.Null(vote.ReviewId);
        Assert.True(vote.IsLike);
    }

    [Fact]
    public async Task Votar_LoPropio_NoGeneraAviso()
    {
        var author = await CreateUserAsync("nvsf");
        var reviewId = await CreateReviewAsync(author);

        await author.Client.PostAsJsonAsync($"/api/reviews/{reviewId}/likes", new { isLike = true });

        Assert.Empty(await GetNotificationsAsync(author.Client));
    }

    // ------------------------------------------------------------------
    // Seguimientos
    // ------------------------------------------------------------------

    [Fact]
    public async Task Seguir_AvisaAlSeguido()
    {
        var follower = await CreateUserAsync("nfwr");
        var target = await CreateUserAsync("nftg");

        await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);

        var notification = Assert.Single(await GetNotificationsAsync(target.Client));

        Assert.Equal(NotificationType.NewFollower, notification.Type);
        Assert.Equal(follower.UserName, notification.Actor!.UserName);
        Assert.Null(notification.ReviewId);
        Assert.Null(notification.CommentId);
    }

    [Fact]
    public async Task Seguir_DejarDeSeguir_YVolverASeguir_NoAcumulaAvisos()
    {
        var follower = await CreateUserAsync("nfrp");
        var target = await CreateUserAsync("nftg");

        await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);
        await follower.Client.DeleteAsync($"/api/users/{target.UserName}/follow");
        await follower.Client.PostAsync($"/api/users/{target.UserName}/follow", null);

        // El aviso sin leer se refresca en lugar de duplicarse.
        Assert.Single(await GetNotificationsAsync(target.Client));
        Assert.Equal(1, await GetUnreadCountAsync(target.Client));
    }

    // ------------------------------------------------------------------
    // Lectura
    // ------------------------------------------------------------------

    [Fact]
    public async Task MarcarComoLeido_EsIdempotente()
    {
        var author = await CreateUserAsync("nmrd");
        var other = await CreateUserAsync("noth");
        var reviewId = await CreateReviewAsync(author);

        await PostCommentAsync(other.Client, reviewId, "Genera el aviso.");

        var notification = Assert.Single(await GetNotificationsAsync(author.Client));

        var first = await author.Client.PostAsync($"/api/notifications/{notification.Id}/read", null);
        var second = await author.Client.PostAsync($"/api/notifications/{notification.Id}/read", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(0, await GetUnreadCountAsync(author.Client));
    }

    [Fact]
    public async Task MarcarComoLeido_ElAvisoDeOtro_DevuelveNotFound()
    {
        // El filtro por destinatario va dentro del UPDATE: tocar el aviso ajeno no es
        // "prohibido", es imposible. La respuesta es 404 y no 403 para no confirmar
        // siquiera que ese aviso existe.
        var author = await CreateUserAsync("nmfo");
        var other = await CreateUserAsync("noth");
        var intruder = await CreateUserAsync("nint");
        var reviewId = await CreateReviewAsync(author);

        await PostCommentAsync(other.Client, reviewId, "Genera el aviso.");

        var notification = Assert.Single(await GetNotificationsAsync(author.Client));

        var response = await intruder.Client.PostAsync(
            $"/api/notifications/{notification.Id}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await GetUnreadCountAsync(author.Client));
    }

    [Fact]
    public async Task MarcarTodo_DevuelveCuantosCambiaronYDejaElContadorEnCero()
    {
        var author = await CreateUserAsync("nall");
        var other = await CreateUserAsync("noth");
        var reviewId = await CreateReviewAsync(author);

        await PostCommentAsync(other.Client, reviewId, "Uno.");
        await PostCommentAsync(other.Client, reviewId, "Dos.");
        await PostCommentAsync(other.Client, reviewId, "Tres.");

        var response = await author.Client.PostAsync("/api/notifications/read-all", null);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MarkAllPayload>(Json);

        Assert.Equal(3, payload!.MarkedAsRead);
        Assert.Equal(0, await GetUnreadCountAsync(author.Client));

        // Una segunda pasada no cambia nada.
        var again = await author.Client.PostAsync("/api/notifications/read-all", null);
        var repeated = await again.Content.ReadFromJsonAsync<MarkAllPayload>(Json);

        Assert.Equal(0, repeated!.MarkedAsRead);
    }

    [Fact]
    public async Task UnreadOnly_FiltraLosYaLeidos()
    {
        var author = await CreateUserAsync("nuor");
        var other = await CreateUserAsync("noth");
        var reviewId = await CreateReviewAsync(author);

        await PostCommentAsync(other.Client, reviewId, "Uno.");
        await PostCommentAsync(other.Client, reviewId, "Dos.");

        var all = await GetNotificationsAsync(author.Client);

        Assert.Equal(2, all.Length);

        await author.Client.PostAsync($"/api/notifications/{all[0].Id}/read", null);

        var unread = await GetNotificationsAsync(author.Client, unreadOnly: true);

        Assert.Single(unread);
        Assert.DoesNotContain(unread, n => n.Id == all[0].Id);
        Assert.Equal(2, (await GetNotificationsAsync(author.Client)).Length);
    }

    // ------------------------------------------------------------------
    // Orden y paginado
    // ------------------------------------------------------------------

    [Fact]
    public async Task Listado_DevuelveDelMasRecienteAlMasViejo()
    {
        var author = await CreateUserAsync("nord");
        var other = await CreateUserAsync("noth");
        var reviewId = await CreateReviewAsync(author);

        await PostCommentAsync(other.Client, reviewId, "Primero.");
        await PostCommentAsync(other.Client, reviewId, "Segundo.");
        await PostCommentAsync(other.Client, reviewId, "Tercero.");

        var items = await GetNotificationsAsync(author.Client);

        Assert.Equal(3, items.Length);

        for (var i = 1; i < items.Length; i++)
        {
            Assert.True(
                items[i - 1].CreatedAt > items[i].CreatedAt
                || (items[i - 1].CreatedAt == items[i].CreatedAt && items[i - 1].Id > items[i].Id),
                "El listado tiene que venir estrictamente descendente por (fecha, Id).");
        }
    }

    [Fact]
    public async Task Listado_PaginadoPorCursor_NoRepiteNiPierdeAvisos()
    {
        var author = await CreateUserAsync("npag");
        var other = await CreateUserAsync("noth");
        var reviewId = await CreateReviewAsync(author);

        for (var i = 0; i < 5; i++)
        {
            await PostCommentAsync(other.Client, reviewId, $"Comentario {i}.");
        }

        var seen = new List<int>();
        string? cursor = null;

        do
        {
            var url = "/api/notifications?limit=2"
                      + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

            var page = await author.Client.GetFromJsonAsync<NotificationPagePayload>(url, Json);

            seen.AddRange(page!.Items.Select(n => n.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null && seen.Count < 20);

        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct().Count());
    }
}
