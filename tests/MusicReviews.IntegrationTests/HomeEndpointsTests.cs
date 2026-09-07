using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// Las secciones del home. La suite comparte base con el resto de los tests, asi que
/// aca no se puede afirmar "mi album esta primero": otro test puede haber generado mas
/// actividad. Lo que si se puede afirmar —y es lo que importa— es el contrato: que el
/// orden sea el declarado, que los pesos se apliquen, que la ventana recorte y que la
/// exclusion por usuario funcione.
/// </summary>
public class HomeEndpointsTests : IntegrationTestBase
{
    public HomeEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record HomeAlbumPayload(
        int Id,
        string MusicBrainzId,
        string Title,
        string? CoverArtUrl,
        string ArtistName,
        string ArtistMusicBrainzId,
        DateOnly? ReleaseDate,
        int ReviewCount,
        double? AverageScore,
        int ActivityScore);

    private sealed record HomePagePayload(
        HomeAlbumPayload[] Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPrevious,
        bool HasNext);

    private sealed record ReviewPayload(int Id);

    private sealed record CommentPayload(int Id);

    private async Task<(int ReviewId, string AlbumMbid)> CreateReviewAsync(TestUser user, int score = 80)
    {
        var mbid = NewMbid();

        var response = await user.Client.PostAsJsonAsync("/api/reviews", new
        {
            albumMusicBrainzId = mbid,
            score,
            text = "Review de soporte para el home."
        });

        response.EnsureSuccessStatusCode();

        var review = await response.Content.ReadFromJsonAsync<ReviewPayload>(Json);

        return (review!.Id, mbid);
    }

    private static async Task<HomePagePayload> GetAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<HomePagePayload>(url, Json))!;

    /// <summary>
    /// Busca un album recorriendo las paginas.
    /// </summary>
    /// <remarks>
    /// Toda la suite comparte base, asi que la cantidad de albumes con actividad crece
    /// con los tests que corrieron antes. Mirar solo la primera pagina haria que estos
    /// tests empezaran a fallar el dia que se agregue el test numero cien, por una razon
    /// que no tiene nada que ver con lo que verifican.
    /// </remarks>
    private static async Task<HomeAlbumPayload?> FindAsync(HttpClient client, string path, string mbid)
    {
        for (var page = 1; page <= 20; page++)
        {
            var result = await GetAsync(client, $"{path}?page={page}&pageSize=100");
            var found = result.Items.SingleOrDefault(album => album.MusicBrainzId == mbid);

            if (found is not null || !result.HasNext)
            {
                return found;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Populares
    // ------------------------------------------------------------------

    [Fact]
    public async Task Populares_EsPublico()
    {
        var response = await CreateClient().GetAsync("/api/home/popular");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Populares_IncluyeUnAlbumRecienReseniado()
    {
        var user = await CreateUserAsync("hpop");
        var (_, mbid) = await CreateReviewAsync(user);

        var album = await FindAsync(CreateClient(), "/api/home/popular", mbid);

        Assert.NotNull(album);
        Assert.Equal(1, album!.ReviewCount);
        Assert.Equal(80d, album.AverageScore);

        // Una reseña pesa 3. Ver HomeDefaults.
        Assert.Equal(3, album.ActivityScore);
    }

    [Fact]
    public async Task Populares_SumaComentariosYVotosConSuPeso()
    {
        var author = await CreateUserAsync("hwei");
        var other = await CreateUserAsync("hoth");
        var (reviewId, mbid) = await CreateReviewAsync(author);

        var comment = await other.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/comments", new { text = "Un comentario." });
        comment.EnsureSuccessStatusCode();

        var vote = await other.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/likes", new { isLike = true });
        vote.EnsureSuccessStatusCode();

        var album = await FindAsync(CreateClient(), "/api/home/popular", mbid);

        // reseña (3) + comentario (2) + voto (1)
        Assert.Equal(6, album!.ActivityScore);
    }

    [Fact]
    public async Task Populares_NoCuentaLosComentariosBorrados()
    {
        var author = await CreateUserAsync("hdel");
        var other = await CreateUserAsync("hoth");
        var (reviewId, mbid) = await CreateReviewAsync(author);

        var created = await other.Client.PostAsJsonAsync(
            $"/api/reviews/{reviewId}/comments", new { text = "Se va a borrar." });
        created.EnsureSuccessStatusCode();

        var comment = await created.Content.ReadFromJsonAsync<CommentPayload>(Json);

        var deleted = await other.Client.DeleteAsync($"/api/comments/{comment!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var album = await FindAsync(CreateClient(), "/api/home/popular", mbid);

        // El hilo sigue mostrando el hueco, pero el album no cobra popularidad por algo
        // que ya no se lee: queda solo el peso de la reseña.
        Assert.Equal(3, album!.ActivityScore);
    }

    [Fact]
    public async Task Populares_VieneOrdenadoDeMayorAMenorActividad()
    {
        var user = await CreateUserAsync("hord");
        await CreateReviewAsync(user);

        var page = await GetAsync(CreateClient(), "/api/home/popular?pageSize=100");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, album => Assert.True(album.ActivityScore > 0));

        var scores = page.Items.Select(album => album.ActivityScore).ToArray();

        Assert.Equal(scores.OrderByDescending(score => score), scores);
    }

    [Fact]
    public async Task Populares_ConVentanaDeCeroDias_SeRecortaAlMinimoYNoRompe()
    {
        // El parametro se recorta al rango permitido en vez de rechazarse: un cliente
        // que manda cualquier cosa recibe la seccion, no un 400 en la portada.
        var response = await CreateClient().GetAsync("/api/home/popular?windowDays=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var conExceso = await CreateClient().GetAsync("/api/home/popular?windowDays=99999");

        Assert.Equal(HttpStatusCode.OK, conExceso.StatusCode);
    }

    [Fact]
    public async Task Populares_PaginaSinRepetirEntrePaginas()
    {
        var user = await CreateUserAsync("hpag");

        for (var i = 0; i < 3; i++)
        {
            await CreateReviewAsync(user, 70 + i);
        }

        var first = await GetAsync(CreateClient(), "/api/home/popular?page=1&pageSize=2");
        var second = await GetAsync(CreateClient(), "/api/home/popular?page=2&pageSize=2");

        Assert.Equal(2, first.Items.Length);
        Assert.NotEmpty(second.Items);

        // El desempate por Id es lo que garantiza esto: sin el, dos albumes con el mismo
        // puntaje pueden intercambiar posiciones entre una pagina y la siguiente.
        Assert.Empty(first.Items.Select(a => a.Id).Intersect(second.Items.Select(a => a.Id)));
    }

    // ------------------------------------------------------------------
    // Explorar
    // ------------------------------------------------------------------

    [Fact]
    public async Task Explorar_EsPublicoYDevuelveAlbumes()
    {
        var user = await CreateUserAsync("hexp");
        await CreateReviewAsync(user);

        var page = await GetAsync(CreateClient(), "/api/home/explore?pageSize=50");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, album => Assert.NotEmpty(album.ArtistName));
    }

    [Fact]
    public async Task Explorar_ConSesion_ExcluyeLoQueElUsuarioYaResenio()
    {
        var user = await CreateUserAsync("hexc");
        var (_, mbid) = await CreateReviewAsync(user);

        // Sin sesion el album aparece: es parte del catalogo como cualquier otro.
        Assert.NotNull(await FindAsync(CreateClient(), "/api/home/explore", mbid));

        // Con la sesion del autor, no: recomendarle lo que ya puntuo no descubre nada.
        Assert.Null(await FindAsync(user.Client, "/api/home/explore", mbid));
    }

    [Fact]
    public async Task Explorar_PoneLosQueTienenPortadaAntesQueLosQueNo()
    {
        var user = await CreateUserAsync("hcov");
        await CreateReviewAsync(user);

        var page = await GetAsync(CreateClient(), "/api/home/explore?pageSize=100");

        // Una grilla de tapas grandes que arranca con recuadros vacios no invita a
        // explorar nada: los que tienen portada van primero.
        var withCover = page.Items.Select(album => album.CoverArtUrl is not null).ToArray();

        Assert.Equal(withCover.OrderByDescending(has => has), withCover);
    }

    [Fact]
    public async Task Explorar_PaginaSinRepetirEntrePaginas()
    {
        var user = await CreateUserAsync("hepg");

        for (var i = 0; i < 3; i++)
        {
            await CreateReviewAsync(user);
        }

        var first = await GetAsync(CreateClient(), "/api/home/explore?page=1&pageSize=2");
        var second = await GetAsync(CreateClient(), "/api/home/explore?page=2&pageSize=2");

        Assert.Equal(2, first.Items.Length);
        Assert.NotEmpty(second.Items);
        Assert.Empty(first.Items.Select(a => a.Id).Intersect(second.Items.Select(a => a.Id)));
        Assert.True(first.HasNext);
        Assert.True(second.HasPrevious);
    }
}
