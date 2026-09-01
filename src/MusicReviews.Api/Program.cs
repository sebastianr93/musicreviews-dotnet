using MusicReviews.Api.Infrastructure;
using MusicReviews.Infrastructure;
using MusicReviews.Infrastructure.Persistence;
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

    builder.Services.AddControllers();

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

    builder.Services.AddOpenApi();

    var app = builder.Build();

    // ---- Pipeline --------------------------------------------------------

    // Primero de todo: cualquier excepcion no manejada sale como ProblemDetails.
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("MusicReviews API")
            .WithTheme(ScalarTheme.Purple));

        await DatabaseInitializer.MigrateAsync(app.Services);
    }

    app.UseHttpsRedirection();

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
