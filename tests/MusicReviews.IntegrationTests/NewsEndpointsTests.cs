using System.Net;
using System.Net.Http.Json;
using MusicReviews.IntegrationTests.Infrastructure;

namespace MusicReviews.IntegrationTests;

/// <summary>
/// El endpoint de noticias. El agregador real esta reemplazado por un fake: lo que se
/// verifica aca es el contrato del endpoint, no el parseo del XML, que lo cubren los
/// tests unitarios de <c>RssParser</c>.
/// </summary>
public class NewsEndpointsTests : IntegrationTestBase
{
    public NewsEndpointsTests(MusicReviewsApiFactory factory) : base(factory) { }

    private sealed record NewsItemPayload(
        string Title,
        string? Excerpt,
        string Url,
        string? ImageUrl,
        string SourceName,
        DateTimeOffset? PublishedAt);

    private async Task<NewsItemPayload[]> GetAsync(string url) =>
        (await CreateClient().GetFromJsonAsync<NewsItemPayload[]>(url, Json))!;

    [Fact]
    public async Task Noticias_EsPublico()
    {
        // Un visitante sin cuenta tiene que poder ver el home completo.
        var response = await CreateClient().GetAsync("/api/news");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Noticias_TraeLoMinimoParaPublicarContenidoAjeno()
    {
        var items = await GetAsync("/api/news");

        Assert.NotEmpty(items);

        Assert.All(items, item =>
        {
            Assert.NotEmpty(item.Title);
            Assert.StartsWith("https://", item.Url);

            // La atribucion no es opcional: sin la fuente, el extracto es contenido
            // ajeno presentado como propio.
            Assert.Equal(FakeNewsService.SourceName, item.SourceName);
        });
    }

    [Fact]
    public async Task Noticias_RespetaElLimite()
    {
        var items = await GetAsync("/api/news?limit=3");

        Assert.Equal(3, items.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(9999)]
    public async Task Noticias_ConUnLimiteFueraDeRango_LoRecortaEnVezDeFallar(int limit)
    {
        // Es una seccion de relleno en la portada: un parametro raro no puede devolver
        // un 400 en el home.
        var response = await CreateClient().GetAsync($"/api/news?limit={limit}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<NewsItemPayload[]>(Json);

        Assert.NotEmpty(items!);
    }

    [Fact]
    public async Task Noticias_VienenDeLaMasNuevaALaMasVieja()
    {
        var items = await GetAsync("/api/news");

        var dates = items.Select(item => item.PublishedAt).ToArray();

        Assert.Equal(dates.OrderByDescending(date => date), dates);
    }
}
