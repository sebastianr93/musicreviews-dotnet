using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicReviews.Infrastructure.Identity;

namespace MusicReviews.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>
    /// Politica del proxy al catalogo. Es la mas restrictiva porque cada request que
    /// no pega en cache consume un turno de la cola de 1 req/seg hacia MusicBrainz:
    /// sin este limite, un solo cliente puede dejar sin catalogo a todos los demas.
    /// </summary>
    public const string Catalog = "catalog";

    /// <summary>Politica general para el resto de la Api publica.</summary>
    public const string Default = "default";
}

public static class RateLimiting
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Catalog, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        // Sin cola: encolar solo traslada la espera al cliente y
                        // mantiene ocupados los hilos del servidor.
                        QueueLimit = 0
                    }));

            options.AddPolicy(RateLimitPolicies.Default, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Demasiadas solicitudes.",
                    Detail = "Superaste el limite de peticiones. Volve a intentar en unos segundos.",
                    Instance = context.HttpContext.Request.Path
                };

                problem.Extensions["code"] = "rate_limit_exceeded";
                problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Particiona por usuario autenticado cuando lo hay, y por IP cuando no.
    /// Usar solo la IP castigaria a todos los usuarios detras de un mismo NAT.
    /// </summary>
    private static string GetPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(AuthClaimTypes.Sub)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return $"ip:{ip}";
    }
}
