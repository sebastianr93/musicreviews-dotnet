using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicReviews.Application.Catalog;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>Registro de los clientes externos del catalogo y del servicio que los usa.</summary>
internal static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddCatalog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MusicBrainzOptions>()
            .Bind(configuration.GetSection(MusicBrainzOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();

        // La cola de salida es singleton: el limite de MusicBrainz es por aplicacion.
        // El handler es transient porque HttpClientFactory recicla la cadena de handlers,
        // y si el estado viviera ahi se perderia (o se dispondria) cada pocos minutos.
        services.AddSingleton<MusicBrainzThrottle>();
        services.AddTransient<MusicBrainzThrottlingHandler>();

        services
            .AddHttpClient<IMusicBrainzClient, MusicBrainzClient>(
                MusicBrainzClient.HttpClientName,
                ConfigureMusicBrainzClient)
            .AddHttpMessageHandler<MusicBrainzThrottlingHandler>();

        services.AddHttpClient<ICoverArtArchiveClient, CoverArtArchiveClient>(
            CoverArtArchiveClient.HttpClientName,
            ConfigureCoverArtClient);

        services.AddHttpClient<IWikidataClient, WikidataClient>(
            WikidataClient.HttpClientName,
            ConfigureWikidataClient);

        services.AddScoped<IMusicCatalogService, MusicCatalogService>();

        return services;
    }

    private static void ConfigureMusicBrainzClient(IServiceProvider services, HttpClient client)
    {
        var options = GetOptions(services);

        client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));

        // Sin timeout global: lo aplica MusicBrainzThrottlingHandler por intento.
        // Con un timeout aca, la espera en la cola de 1 req/seg lo consumiria y el
        // error resultante culparia al HttpClient en vez de al servicio externo.
        client.Timeout = Timeout.InfiniteTimeSpan;

        // TryAddWithoutValidation y no ParseAdd: el formato que pide MusicBrainz
        // incluye un comentario entre parentesis con la via de contacto, y el parser
        // estricto de .NET lo rechaza segun como este escrito.
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    private static void ConfigureCoverArtClient(IServiceProvider services, HttpClient client)
    {
        var options = GetOptions(services);

        client.BaseAddress = new Uri(EnsureTrailingSlash(options.CoverArtBaseUrl));
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
    }

    /// <remarks>
    /// Wikidata no tiene la cola de 1 request por segundo de MusicBrainz, pero si pide
    /// identificarse: sin User-Agent devuelve 403. Se reutiliza el mismo que ya exige
    /// MusicBrainz en vez de configurar otro.
    /// </remarks>
    private static void ConfigureWikidataClient(IServiceProvider services, HttpClient client)
    {
        var options = GetOptions(services);

        client.BaseAddress = new Uri("https://www.wikidata.org/");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    private static MusicBrainzOptions GetOptions(IServiceProvider services) =>
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MusicBrainzOptions>>().Value;

    /// <summary>
    /// Sin la barra final, <see cref="Uri"/> descarta el ultimo segmento al combinar
    /// con una ruta relativa: "https://musicbrainz.org/ws/2" + "artist" daria
    /// "https://musicbrainz.org/ws/artist".
    /// </summary>
    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
