using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicReviews.Infrastructure.Persistence;

namespace MusicReviews.Infrastructure;

/// <summary>
/// Punto unico de registro de la capa de infraestructura.
/// La capa Api no conoce Npgsql ni EF Core: solo llama a <c>AddInfrastructure</c>.
/// </summary>
public static class DependencyInjection
{
    public const string DefaultConnectionName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var connectionString = configuration.GetConnectionString(DefaultConnectionName)
            ?? throw new InvalidOperationException(
                $"Falta la cadena de conexion '{DefaultConnectionName}'. " +
                "Definila en appsettings.Development.json o en la variable de entorno " +
                $"ConnectionStrings__{DefaultConnectionName}.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                // Reintentos ante fallos transitorios de red/conexion.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            if (isDevelopment)
            {
                // Solo en desarrollo: los valores de parametros pueden contener datos personales.
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            }
        });

        return services;
    }
}
