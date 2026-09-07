using FluentValidation;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Auth.Validators;
using MusicReviews.Application.Common.Interfaces;
using MusicReviews.Infrastructure;
using MusicReviews.Infrastructure.Persistence;
using MusicReviews.Infrastructure.Persistence.Seed;
using MusicReviews.Infrastructure.Users;
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

    // Primero de todo: cualquier excepcion no manejada sale como ProblemDetails.
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging();

    // El frontend se sirve desde wwwroot, en el mismo origen que la Api: sin CORS,
    // sin segundo proceso, sin paso de build. UseDefaultFiles hace que "/" resuelva
    // a index.html; va antes que UseStaticFiles porque solo reescribe la ruta.
    app.UseDefaultFiles();

    var isDevelopment = app.Environment.IsDevelopment();

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

    // Los avatares subidos se sirven desde su propia carpeta, fuera de wwwroot.
    MapAvatars(app, isDevelopment);

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("MusicReviews API")
            .WithTheme(ScalarTheme.Purple));

        await DatabaseInitializer.MigrateAsync(app.Services);
        await IdentitySeeder.SeedAsync(app.Services);
    }
    else
    {
        // Fuera de desarrollo el perfil siempre expone HTTPS; en local el perfil http
        // no tiene puerto seguro al que redirigir y el middleware solo loguea un warning.
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    // Despues de la autenticacion: asi el limitador puede particionar por usuario
    // y no castigar a todos los que comparten una IP.
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseRateLimiter();

    app.MapControllers();

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
/// Sirve los avatares subidos desde su carpeta, con las defensas que un archivo de
/// usuario necesita y los estaticos del sitio no.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fuera de wwwroot.</b> Ahi adentro los archivos de los usuarios quedarian dentro
/// del publicado —se perderian en cada despliegue— y mezclados con los del sitio.
/// </para>
/// <para>
/// <b>Solo tipos de imagen.</b> Se limpia el mapa de extensiones y se declaran a mano
/// las cuatro aceptadas. Con el mapa por defecto, cualquier archivo que se colara en la
/// carpeta se serviria con su tipo "correcto"; con este, lo que no este en la lista no
/// se sirve en absoluto.
/// </para>
/// <para>
/// <b>nosniff.</b> Sin esa cabecera, el navegador puede ignorar el Content-Type y
/// decidir por su cuenta que un archivo es HTML. Servido desde nuestro propio dominio,
/// eso es un XSS con nuestro origen. La validacion por bytes magicos al subir y esta
/// cabecera al servir son las dos mitades de la misma defensa.
/// </para>
/// </remarks>
static void MapAvatars(WebApplication app, bool isDevelopment)
{
    var options = app.Services.GetRequiredService<IOptions<AvatarOptions>>().Value;
    var fullPath = Path.GetFullPath(options.StoragePath);

    Directory.CreateDirectory(fullPath);

    var contentTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string>
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif"
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(fullPath),
        RequestPath = options.RequestPath.TrimEnd('/'),
        ContentTypeProvider = contentTypes,
        ServeUnknownFileTypes = false,
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers.XContentTypeOptions = "nosniff";

            // El nombre del archivo cambia con cada subida, asi que la URL vieja deja de
            // existir cuando alguien cambia su foto: se puede cachear sin miedo. En
            // desarrollo igual se revalida, por el mismo motivo que el resto de los
            // estaticos.
            context.Context.Response.Headers.CacheControl = isDevelopment
                ? "no-cache, must-revalidate"
                : "public, max-age=31536000, immutable";
        }
    });
}

/// <summary>
/// Expuesta para que los tests de integracion puedan usar WebApplicationFactory&lt;Program&gt;.
/// </summary>
public partial class Program;
