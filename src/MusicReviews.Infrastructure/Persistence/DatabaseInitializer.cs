using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MusicReviews.Infrastructure.Persistence;

/// <summary>
/// Aplica las migraciones pendientes al arrancar. Pensado para desarrollo:
/// en produccion las migraciones se aplican como paso explicito del despliegue,
/// no desde el proceso de la aplicacion.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Base de datos al dia, no hay migraciones pendientes.");
            return;
        }

        logger.LogInformation(
            "Aplicando {Count} migracion(es) pendiente(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));

        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Migraciones aplicadas correctamente.");
    }
}
