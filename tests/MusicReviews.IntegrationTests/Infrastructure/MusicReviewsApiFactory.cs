using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
// ConfigureTestServices vive aca, no en el namespace de WebApplicationFactory.
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicReviews.Application.Catalog;
using MusicReviews.Application.News;
using MusicReviews.Application.Users;
using Testcontainers.PostgreSql;

namespace MusicReviews.IntegrationTests.Infrastructure;

/// <summary>
/// Levanta la Api completa en memoria contra un PostgreSQL real en Docker.
/// </summary>
/// <remarks>
/// Se usa Postgres de verdad y no un proveedor en memoria: buena parte de lo que hay
/// que verificar vive en la base y no existe en InMemory — los indices unicos que
/// sostienen "una review por album" y "un voto por objetivo", los CHECK del puntaje,
/// el borrado en cascada, y la traduccion real de las proyecciones a SQL. Un test
/// contra InMemory pasaria con un modelo que en produccion falla.
///
/// El contenedor se comparte entre todas las clases de test (ver ApiTestCollection):
/// arrancar uno por clase sumaria segundos de arranque sin aislar mas, porque cada
/// test usa datos propios con nombres unicos.
/// </remarks>
public sealed class MusicReviewsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUserName = "testadmin";
    public const string AdminEmail = "admin@musicreviews.tests";
    public const string AdminPassword = "AdminTest1234";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("musicreviews_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development es el unico entorno en el que Program.cs aplica migraciones y
        // siembra roles al arrancar, que es justo lo que necesita la base del contenedor.
        builder.UseEnvironment("Development");

        // ConfigureAppConfiguration y no UseSetting: esta fuente se agrega al final,
        // asi que gana sobre appsettings.Development.json. Con UseSetting, la cadena
        // de conexion del appsettings pisaria la del contenedor y los tests correrian
        // contra la base de desarrollo.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _database.GetConnectionString(),

                ["Jwt:Issuer"] = "MusicReviews.Tests",
                ["Jwt:Audience"] = "MusicReviews.Tests",
                ["Jwt:SigningKey"] = "clave-de-firma-exclusiva-para-tests-de-integracion",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "7",

                ["SeedAdmin:UserName"] = AdminUserName,
                ["SeedAdmin:Email"] = AdminEmail,
                ["SeedAdmin:Password"] = AdminPassword,

                // Ruido innecesario en la salida de los tests.
                ["Serilog:MinimumLevel:Default"] = "Warning"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMusicCatalogService>();
            services.AddScoped<IMusicCatalogService, FakeMusicCatalogService>();

            // Las noticias tampoco salen a internet durante los tests: un medio lento o
            // caido volveria la suite lenta y roja por algo que no es nuestro.
            services.RemoveAll<INewsService>();
            services.AddScoped<INewsService, FakeNewsService>();

            // Los avatares no se escriben en disco durante los tests.
            services.RemoveAll<IAvatarStorage>();
            services.AddSingleton<IAvatarStorage, FakeAvatarStorage>();
        });
    }

    public async Task InitializeAsync() => await _database.StartAsync();

    // Explicito para no chocar con el DisposeAsync() de WebApplicationFactory,
    // que devuelve ValueTask y tiene otra firma.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

/// <summary>
/// Agrupa todas las clases de test en una sola coleccion: xUnit paraleliza entre
/// colecciones, y dos suites escribiendo en la misma base a la vez producirian
/// fallos intermitentes imposibles de reproducir.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiTestCollection : ICollectionFixture<MusicReviewsApiFactory>
{
    public const string Name = "api";
}
