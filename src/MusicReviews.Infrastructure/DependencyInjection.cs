using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicReviews.Application.Activity;
using MusicReviews.Application.Admin;
using MusicReviews.Application.Auth;
using MusicReviews.Application.Comments;
using MusicReviews.Application.Home;
using MusicReviews.Application.Likes;
using MusicReviews.Application.Notifications;
using MusicReviews.Application.Reviews;
using MusicReviews.Application.Search;
using MusicReviews.Application.Users;
using MusicReviews.Domain.Constants;
using MusicReviews.Domain.Entities;
using MusicReviews.Infrastructure.Activity;
using MusicReviews.Infrastructure.Admin;
using MusicReviews.Infrastructure.Catalog;
using MusicReviews.Infrastructure.Comments;
using MusicReviews.Infrastructure.Home;
using MusicReviews.Infrastructure.Identity;
using MusicReviews.Infrastructure.Likes;
using MusicReviews.Infrastructure.News;
using MusicReviews.Infrastructure.Notifications;
using MusicReviews.Infrastructure.Persistence;
using MusicReviews.Infrastructure.Persistence.Seed;
using MusicReviews.Infrastructure.Reviews;
using MusicReviews.Infrastructure.Search;
using MusicReviews.Infrastructure.Users;

namespace MusicReviews.Infrastructure;

/// <summary>
/// Punto unico de registro de la capa de infraestructura.
/// La capa Api no conoce Npgsql, EF Core ni la configuracion de JWT: solo llama a
/// <c>AddInfrastructure</c>.
/// </summary>
/// <remarks>
/// Regla que se sigue en todo este archivo: <b>nada lee la configuracion al registrar
/// servicios</b>. Los valores se toman siempre desde el contenedor, en el momento en que
/// el servicio se construye. Leerlos antes captura una foto de la configuracion que
/// puede quedar desactualizada si se agrega una fuente despues —lo que hace
/// <c>WebApplicationFactory</c> en los tests— y el sintoma es dificil de rastrear:
/// la app termina firmando tokens con una clave y validandolos con otra, o escribiendo
/// en una base distinta de la configurada.
/// </remarks>
public static class DependencyInjection
{
    public const string DefaultConnectionName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.TryAddSingleton(TimeProvider.System);

        services
            .AddPersistence(isDevelopment)
            .AddIdentityServices()
            .AddJwtAuthentication(configuration)
            .AddCatalog(configuration)
            .AddNews(configuration)
            .AddAvatars(configuration)
            .AddDemoContent(configuration)
            .AddApplicationServices();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, bool isDevelopment)
    {
        // La cadena de conexion se resuelve desde el proveedor, no desde la
        // IConfiguration capturada al registrar: ver el comentario de la clase.
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            var connectionString = configuration.GetConnectionString(DefaultConnectionName)
                ?? throw new InvalidOperationException(
                    $"Falta la cadena de conexion '{DefaultConnectionName}'. " +
                    "Definila en appsettings.Development.json o en la variable de entorno " +
                    $"ConnectionStrings__{DefaultConnectionName}.");

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

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

    private static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        // AddIdentityCore (no AddIdentity): la Api es stateless y autentica por JWT.
        // AddIdentity registraria ademas el esquema de cookies y lo dejaria como esquema
        // por defecto, que es la causa clasica de que un endpoint protegido responda con
        // un redirect a /Account/Login en vez de un 401.
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    private static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ValidateOnStart + DataAnnotations: si falta la clave o es mas corta que lo que
        // exige HMAC-SHA256, la aplicacion no arranca en vez de emitir tokens inseguros
        // en silencio.
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

        // Los TokenValidationParameters se arman desde IOptions<JwtOptions>, la misma
        // instancia que usa TokenService para firmar. Ver ConfigureJwtBearerOptions.
        services.ConfigureOptions<ConfigureJwtBearerOptions>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.RequireAdmin, policy => policy.RequireRole(AppRoles.Admin));

        return services;
    }

    /// <summary>
    /// Registra el sembrador de contenido de demostracion.
    /// </summary>
    /// <remarks>
    /// Se registra siempre y decide en tiempo de ejecucion si tiene algo que hacer.
    /// Condicionar el registro obligaria a leer la configuracion aca, que es justo lo que
    /// este archivo no hace (ver el comentario de la clase), y ademas dejaria a los tests
    /// de integracion con un contenedor distinto del de produccion.
    /// </remarks>
    private static IServiceCollection AddDemoContent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName));

        services.AddHostedService<DemoSeeder>();

        return services;
    }

    /// <summary>
    /// Almacenamiento de avatares: los bytes van a la base, no a disco. Ver
    /// <see cref="DbAvatarStorage"/> para el motivo.
    /// </summary>
    private static IServiceCollection AddAvatars(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AvatarOptions>()
            .Bind(configuration.GetSection(AvatarOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped y no Singleton: la implementacion escribe en el DbContext de la
        // peticion, para que la imagen y el AvatarUrl que la apunta se confirmen juntos.
        services.AddScoped<IAvatarStorage, DbAvatarStorage>();

        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<ILikeService, LikeService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IHomeService, HomeService>();
        services.AddScoped<IQuickSearchService, QuickSearchService>();

        // Una sola implementacion detras de dos interfaces, registrada por su tipo
        // concreto y reenviada: asi el servicio que escribe el aviso y el que lo lee son
        // la misma instancia dentro de la request, y el reenvio no crea una segunda.
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<INotificationWriter>(sp => sp.GetRequiredService<NotificationService>());

        return services;
    }
}

/// <summary>Nombres de las politicas de autorizacion.</summary>
public static class AuthPolicies
{
    public const string RequireAdmin = "RequireAdmin";
}
