using FluentValidation;
using MusicReviews.Api.Infrastructure;
using MusicReviews.Application.Auth.Validators;
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
    app.UseStaticFiles();

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
/// Expuesta para que los tests de integracion puedan usar WebApplicationFactory&lt;Program&gt;.
/// </summary>
public partial class Program;
