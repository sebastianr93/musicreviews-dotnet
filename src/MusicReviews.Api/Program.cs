using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Auth.Validators;
using MusicReviews.Application.Common;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Infrastructure;
using MusicReviews.Infrastructure.Persistence;
using MusicReviews.Infrastructure.Persistence.Seed;
using Scalar.AspNetCore;
using Serilog;

// Logger de arranque: captura errores que ocurren antes de que se lea la configuracion.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Iniciando MusicReviews.Api");

    var builder = WebApplication.CreateBuilder(args);

    // Traduce las convenciones de las plataformas de hosting a las de ASP.NET.
    // Va antes de registrar nada porque toca la configuracion que despues leen todos.
    ConfigureHostingPlatform(builder);

    // Serilog toma su configuracion de appsettings.json (seccion "Serilog").
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---- Servicios -------------------------------------------------------
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddApiRateLimiting();

    // Detras del proxy de la plataforma, la peticion llega por http y con la IP del
    // balanceador. Sin esto pasan dos cosas: el limitador por IP ve una sola direccion
    // para todo el trafico —y castiga a todos juntos— y UseHttpsRedirection cree que la
    // peticion es insegura y responde un redirect a https que vuelve a entrar igual, en
    // un bucle infinito.
    //
    // Se vacian KnownNetworks y KnownProxies porque en estas plataformas la IP del
    // balanceador es dinamica y no se puede declarar. Es aceptable aca: el unico camino
    // de entrada al contenedor es el proxy de la plataforma, asi que las cabeceras no
    // pueden venir de un cliente cualquiera.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Registra todos los validadores del assembly de Application.
    builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
    });

    // Respuestas de error consistentes en formato ProblemDetails (RFC 9457).
    builder.Services.AddProblemDetails(options =>
    {
        options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance = context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        };
    });

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    });

    var app = builder.Build();

    // ---- Pipeline --------------------------------------------------------

    // Lo primero de todo: reescribir el esquema y la IP con lo que dice el proxy.
    // Cualquier middleware que las lea despues —el limitador, la redireccion a https,
    // el log de peticiones— tiene que ver los valores reales.
    app.UseForwardedHeaders();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging();

    var isDevelopment = app.Environment.IsDevelopment();

    if (!isDevelopment && app.Configuration.GetValue("Hosting:HttpsRedirection", true))
    {
        // Se puede apagar por configuracion: hay plataformas que ya redirigen en el borde
        // y no mandan X-Forwarded-Proto, y en esas la redireccion de aca es un bucle.
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    // El frontend se sirve desde wwwroot, en el mismo origen que la Api: sin CORS,
    // sin segundo proceso, sin paso de build. UseDefaultFiles hace que "/" resuelva
    // a index.html; va antes que UseStaticFiles porque solo reescribe la ruta.
    app.UseDefaultFiles();

    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            if (!isDevelopment)
            {
                return;
            }

            // En desarrollo se obliga a revalidar cada archivo estatico.
            //
            // UseStaticFiles manda ETag y Last-Modified pero ningun Cache-Control, y ante
            // esa ausencia el navegador aplica cache heuristica: se queda con la copia que
            // tiene sin preguntar. Con modulos ES eso no degrada, rompe: si app.js llega
            // nuevo y api.js sale de la cache viejo, el import falla en el enlace
            // ("does not provide an export named X") y NO se ejecuta absolutamente nada
            // —ni la barra de navegacion ni el router—, con la pagina en blanco y un solo
            // error en consola que no apunta al archivo culpable.
            //
            // "no-cache" no significa "no guardes": significa "guarda pero pregunta
            // antes de usar". El ETag sigue trabajando, asi que lo que no cambio vuelve
            // como 304 sin cuerpo. En produccion no se toca: ahi el cacheo agresivo es
            // lo que se quiere.
            context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
        }
    });

    // La documentacion queda publica tambien fuera de desarrollo. Es una decision
    // deliberada y acotada a este proyecto: la Api es publica de lectura y su superficie
    // es exactamente la que muestra el documento, asi que esconderlo no agrega seguridad
    // y si quita la forma mas rapida de que alguien entienda que hace el backend.
    // En un sistema con datos de terceros la decision seria la contraria.
    if (app.Configuration.GetValue("OpenApi:Expose", true))
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("MusicReviews API")
            .WithTheme(ScalarTheme.Purple));
    }

    // Despues de la autenticacion: asi el limitador puede particionar por usuario
    // y no castigar a todos los que comparten una IP.
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseRateLimiter();

    app.MapControllers();

    await InitializeDatabaseAsync(app);

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "La aplicacion termino de forma inesperada");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Adapta las convenciones de las plataformas de hosting (Railway, Render, Fly, Neon)
/// a las que espera ASP.NET Core.
/// </summary>
/// <remarks>
/// <para>
/// <b>PORT.</b> La plataforma elige el puerto y lo pasa por esa variable; el contenedor
/// tiene que escuchar ahi y en <c>0.0.0.0</c>, no en localhost, o el balanceador no lo
/// alcanza y el despliegue queda marcado como "unhealthy" sin un solo error en el log.
/// </para>
/// <para>
/// <b>DATABASE_URL.</b> Formato URI, que Npgsql no acepta. Se traduce y se escribe en
/// <c>ConnectionStrings:DefaultConnection</c>, que es de donde lee el resto de la
/// aplicacion: asi Infrastructure no se entera de nada de esto.
/// </para>
/// <para>
/// Las dos son adaptaciones, no configuracion propia: si las variables no estan —el caso
/// de desarrollo local y el de los tests— la funcion no toca nada.
/// </para>
/// </remarks>
static void ConfigureHostingPlatform(WebApplicationBuilder builder)
{
    var port = Environment.GetEnvironmentVariable("PORT");

    if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var parsedPort))
    {
        builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
    }

    var configured = builder.Configuration.GetConnectionString(DependencyInjection.DefaultConnectionName);
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

    // La cadena explicita gana: quien la define a mano lo hace para pisar la de la
    // plataforma, no para que la plataforma lo pise a el.
    var source = !string.IsNullOrWhiteSpace(configured) ? configured : databaseUrl;

    if (DatabaseUrl.IsUri(source))
    {
        builder.Configuration[$"ConnectionStrings:{DependencyInjection.DefaultConnectionName}"] =
            DatabaseUrl.ToConnectionString(source!);
    }
    else if (string.IsNullOrWhiteSpace(configured) && !string.IsNullOrWhiteSpace(databaseUrl))
    {
        builder.Configuration[$"ConnectionStrings:{DependencyInjection.DefaultConnectionName}"] = databaseUrl;
    }
}

/// <summary>
/// Deja la base lista para recibir peticiones: migraciones, roles y, si se pide, el
/// contenido de demostracion.
/// </summary>
/// <remarks>
/// <para>
/// Migrar desde el proceso de la aplicacion no es lo que corresponde en un sistema con
/// varias instancias —dos arrancando a la vez compiten por el mismo lock— ni cuando una
/// migracion puede tardar minutos. Aca se hace igual, y a proposito: es una sola
/// instancia, las migraciones son de segundos, y la alternativa —un paso manual despues
/// de cada despliegue— es la forma mas confiable de que la version nueva del codigo
/// termine hablando con el esquema viejo.
/// </para>
/// <para>
/// Se puede apagar con <c>Database:MigrateOnStartup=false</c> para el dia en que eso
/// cambie.
/// </para>
/// </remarks>
static async Task InitializeDatabaseAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        return;
    }

    await DatabaseInitializer.MigrateAsync(app.Services);
    await IdentitySeeder.SeedAsync(app.Services);
}

/// <summary>
/// Expuesta para que los tests de integracion puedan usar WebApplicationFactory&lt;Program&gt;.
/// </summary>
public partial class Program;
