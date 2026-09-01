using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Aplica la cola de <see cref="MusicBrainzThrottle"/> a cada request saliente y
/// reintenta ante 503, que es como MusicBrainz senializa que se paso el limite.
/// </summary>
public sealed class MusicBrainzThrottlingHandler : DelegatingHandler
{
    private readonly MusicBrainzThrottle _throttle;
    private readonly IOptionsMonitor<MusicBrainzOptions> _options;
    private readonly ILogger<MusicBrainzThrottlingHandler> _logger;

    public MusicBrainzThrottlingHandler(
        MusicBrainzThrottle throttle,
        IOptionsMonitor<MusicBrainzOptions> options,
        ILogger<MusicBrainzThrottlingHandler> logger)
    {
        _throttle = throttle;
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var maxRetries = _options.CurrentValue.MaxRetries;

        for (var attempt = 0; ; attempt++)
        {
            await _throttle.WaitForSlotAsync(cancellationToken);

            // Un HttpRequestMessage no se puede enviar dos veces: cada reintento
            // necesita su propia copia.
            var attemptRequest = attempt == 0 ? request : Clone(request);

            var response = await base.SendAsync(attemptRequest, cancellationToken);

            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt >= maxRetries)
            {
                return response;
            }

            var delay = GetRetryDelay(response, attempt, _throttle.MinInterval);

            _logger.LogWarning(
                "MusicBrainz respondio 503 en {Uri}. Reintento {Attempt}/{Max} en {Delay}.",
                request.RequestUri,
                attempt + 1,
                maxRetries,
                delay);

            response.Dispose();

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Backoff exponencial, salvo que el servidor indique un Retry-After.</summary>
    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt, TimeSpan minInterval)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return minInterval * Math.Pow(2, attempt);
    }

    /// <summary>
    /// Copia el request para un reintento. El catalogo solo hace GET sin cuerpo,
    /// asi que alcanza con clonar metodo, URI y cabeceras.
    /// </summary>
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
