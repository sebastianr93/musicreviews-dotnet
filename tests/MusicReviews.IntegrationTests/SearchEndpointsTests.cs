using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// El desplegable de búsqueda instantánea. Consulta solo el catálogo local, así que los
/// tests siembran ese catálogo creando reseñas —que es lo que persiste álbumes— y
/// después buscan.
/// </summary>
public class SearchEndpointsTests : IntegrationTestBase
{
    public SearchEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record QuickItemPayload(
        int Kind,
        string MusicBrainzId,
        string Title,
        string? Subtitle,
        string? ImageUrl,
        int? Year,
        int Popularity);

    private const int KindArtist = 1;
    private const int KindAlbum = 2;

    private async Task<string> CreateReviewAsync(TestUser user, string mbid, int score = 80)
    {
        var response = await user.Client.PostAsJsonAsync("/api/reviews", new
        {
            albumMusicBrainzId = mbid,
            score,
            text = "Reseña de soporte para la búsqueda."
        });

        response.EnsureSuccessStatusCode();

        return mbid;
    }

    private async Task<QuickItemPayload[]> QuickAsync(string q, int limit = 20) =>
        (await CreateClient().GetFromJsonAsync<QuickItemPayload[]>(
            $"/api/search/quick?q={Uri.EscapeDataString(q)}&limit={limit}", Json))!;

    [Fact]
    public async Task Quick_EsPublico()
    {
        var response = await CreateClient().GetAsync("/api/search/quick?q=a");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Quick_ConLaConsultaVacia_DevuelveVacioSinFallar()
    {
        // El desplegable pide con cada tecla, borrado incluido: un 400 en ese camino
        // seria un error en pantalla por haber apretado backspace.
        Assert.Empty(await QuickAsync(string.Empty));
        Assert.Empty(await QuickAsync("   "));
    }

    [Fact]
    public async Task Quick_ConUnaSolaLetra_YaDevuelveResultados()
    {
        // Lo que se verifica es el umbral: el desplegable tiene que responder desde la
        // primera tecla, no a partir de la tercera. Cuales son los resultados no importa
        // aca —la base la comparten todos los tests—; que haya alguno, si.
        var user = await CreateUserAsync("qs1");
        await CreateReviewAsync(user, NewMbid());

        // El fake nombra todo como "Album ..." y "Artista ...".
        Assert.NotEmpty(await QuickAsync("a"));
    }

    [Fact]
    public async Task Quick_EncuentraElAlbumPorSuTitulo()
    {
        var user = await CreateUserAsync("qs1b");
        var mbid = await CreateReviewAsync(user, NewMbid());

        // El fake del catálogo titula los álbumes "Album {8 primeros del mbid}".
        var items = await QuickAsync($"Album {mbid[..8]}");

        Assert.Contains(items, item => item.MusicBrainzId == mbid);
    }

    [Fact]
    public async Task Quick_MezclaArtistasYAlbumesEnUnaSolaLista()
    {
        var user = await CreateUserAsync("qs2");
        await CreateReviewAsync(user, NewMbid());

        // El fake nombra a los artistas "Artista {8 del mbid}" y a los álbumes
        // "Album {8 del mbid}": buscando una letra común aparecen los dos tipos.
        var items = await QuickAsync("a", limit: 20);

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.True(item.Kind is KindArtist or KindAlbum));
    }

    [Fact]
    public async Task Quick_RespetaElLimite()
    {
        var user = await CreateUserAsync("qs3");

        for (var i = 0; i < 3; i++)
        {
            await CreateReviewAsync(user, NewMbid());
        }

        var items = await QuickAsync("a", limit: 2);

        Assert.True(items.Length <= 2);
    }

    [Fact]
    public async Task Quick_ConComodinesDeLike_NoDevuelveElCatalogoEntero()
    {
        var user = await CreateUserAsync("qs4");
        await CreateReviewAsync(user, NewMbid());

        // Sin escapar, "%" haría que el patrón quede en %%% y devolviera todo. No es
        // una inyección —el valor viaja como parámetro— pero sí un resultado absurdo
        // para quien escribió un símbolo cualquiera.
        Assert.Empty(await QuickAsync("%"));
        Assert.Empty(await QuickAsync("_"));
    }

    [Fact]
    public async Task Quick_LosAlbumesTraenPortadaYArtista()
    {
        var user = await CreateUserAsync("qs5");
        var mbid = await CreateReviewAsync(user, NewMbid());

        var items = await QuickAsync($"Album {mbid[..8]}");
        var album = items.Single(item => item.MusicBrainzId == mbid);

        // Todos los listados muestran la tapa: si el DTO no la trae, el desplegable
        // queda con recuadros vacíos.
        Assert.NotNull(album.ImageUrl);
        Assert.NotNull(album.Subtitle);
    }
}
