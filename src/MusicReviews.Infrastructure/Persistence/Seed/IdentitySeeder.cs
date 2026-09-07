using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicReviews.Domain.Constants;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Seed;

/// <summary>
/// Siembra los roles de la aplicacion y, opcionalmente, una cuenta de administrador inicial.
/// </summary>
/// <remarks>
/// Las credenciales del admin se leen de la seccion "SeedAdmin" de la configuracion.
/// Si no estan definidas, no se crea ninguna cuenta: no hay usuario por defecto con
/// contraseña conocida, que es la forma habitual de dejar un backdoor abierto sin querer.
/// En desarrollo se cargan con user-secrets:
/// <code>
/// dotnet user-secrets set "SeedAdmin:Email" "admin@local" --project src/MusicReviews.Api
/// dotnet user-secrets set "SeedAdmin:Password" "..." --project src/MusicReviews.Api
/// </code>
/// </remarks>
public static class IdentitySeeder
{
    public const string SeedAdminSection = "SeedAdmin";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IdentitySeeder));

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
                logger.LogInformation("Rol creado: {Role}", role);
            }
        }

        await SeedAdminAsync(userManager, configuration, logger);
    }

    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var email = configuration[$"{SeedAdminSection}:Email"];
        var password = configuration[$"{SeedAdminSection}:Password"];
        var userName = configuration[$"{SeedAdminSection}:UserName"] ?? "admin";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "No hay credenciales en '{Section}': no se siembra cuenta de administrador.",
                SeedAdminSection);
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(admin, password);

        if (!result.Succeeded)
        {
            logger.LogError(
                "No se pudo crear la cuenta de administrador: {Errors}",
                string.Join(" ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRolesAsync(admin, [AppRoles.User, AppRoles.Admin]);

        logger.LogInformation("Cuenta de administrador creada: {Email}", email);
    }
}
