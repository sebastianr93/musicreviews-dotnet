using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MusicReviews.Application.News;

namespace MusicReviews.Infrastructure.News;

/// <summary>Registro del agregador de noticias.</summary>
internal static class NewsServiceCollectionExtensions
{
    public static IServiceCollection AddNews(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ValidateOnStart pero SIN exigir feeds: la seccion de noticias es opcional y
        // una lista vacia es una configuracion valida —el servicio devuelve nada y el
        // frontend oculta la seccion—. Lo que si se valida es que un feed configurado
        // tenga nombre y URL, porque uno a medio cargar es un error de configuracion.
        services
            .AddOptions<NewsOptions>()
            .Bind(configuration.GetSection(NewsOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.Feeds.TrueForAll(feed =>
                    !string.IsNullOrWhiteSpace(feed.Name)
                    && Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)),
                "Cada feed de la seccion 'News' necesita un Name y una Url http/https absoluta.")
            .ValidateOnStart();

        services.AddHttpClient<INewsService, NewsService>(ConfigureClient);

        return services;
    }

    private static void ConfigureClient(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<NewsOptions>>().Value;

        // Sin timeout global: lo aplica NewsService por feed, porque el mismo cliente
        // atiende a todas las fuentes y el presupuesto es de cada una.
        client.Timeout = Timeout.InfiniteTimeSpan;

        // Varios medios rechazan al cliente HTTP por defecto de .NET, y algunos
        // devuelven 403 si no llega ningun User-Agent.
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "application/rss+xml, application/atom+xml, application/xml, text/xml");
    }
}
