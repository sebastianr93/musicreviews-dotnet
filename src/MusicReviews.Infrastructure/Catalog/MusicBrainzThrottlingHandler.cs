using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicReviews.Application.Common.Exceptions;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Aplica la cola de <see cref="MusicBrainzThrottle"/> a cada request saliente,
/// reintenta ante 503 —que es como MusicBrainz senializa el exceso de peticiones— y
/// traduce cualquier fallo de transporte a <see cref="ExternalServiceUnavailableException"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que el timeout se aplica aca y no en HttpClient.Timeout.</b> La espera en la
/// cola ocurre DENTRO del pipeline del HttpClient, asi que con un timeout global esa
/// espera consume el mismo presupuesto que la llamada real. Con la cola ocupada y los
/// reintentos con backoff (1.1s, 2.2s, 4.4s), un request puede agotar 20 segundos sin
/// haber llegado a hacer una sola peticion HTTP, y el error resultante —"the request
/// was canceled due to the configured HttpClient.Timeout"— apunta al lugar equivocado.
/// </para>
/// <para>
/// Por eso el HttpClient se configura sin timeout y cada INTENTO recibe el suyo, con un
/// token enlazado al de la request. Distinguir cual de los dos tokens cancelo importa:
/// si fue el nuestro es un timeout del servicio externo (503 al cliente), si fue el de
/// la request es el usuario que se fue (no hay nada que reportar).
/// </para>
/// </remarks>
public sealed class MusicBrainzThrottlingHandler : DelegatingHandler
{
    public const string ServiceName = "MusicBrainz";

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
        var options = _options.CurrentValue;
        var attemptTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var started = Stopwatch.GetTimestamp();

        for (var attempt = 0; ; attempt++)
        {
            await _throttle.WaitForSlotAsync(cancellationToken);

            // Un HttpRequestMessage no se puede enviar dos veces: cada reintento
            // necesita su propia copia.
            var attemptRequest = attempt == 0 ? request : Clone(request);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(attemptTimeout);

            HttpResponseMessage response;

            try
            {
                response = await base.SendAsync(attemptRequest, attemptCts.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Cancelo nuestro token, no el de la request: es un timeout del servicio.
                _logger.LogWarning(
                    "MusicBrainz no respondio en {Timeout} para {Uri} (intento {Attempt}).",
                    attemptTimeout, request.RequestUri, attempt + 1);

                if (attempt >= options.MaxRetries)
                {
                    throw ExternalServiceUnavailableException.Timeout(
                        ServiceName, Stopwatch.GetElapsedTime(started), ex);
                }

                continue;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Fallo de transporte hacia MusicBrainz en {Uri}.", request.RequestUri);

                if (attempt >= options.MaxRetries)
                {
                    throw ExternalServiceUnavailableException.Transport(ServiceName, ex);
                }

                await Task.Delay(GetRetryDelay(null, attempt, _throttle.MinInterval), cancellationToken);
                continue;
            }

            // 503 es como MusicBrainz senializa que se paso el limite; el resto de los
            // 5xx son problemas suyos que un reintento tampoco va a resolver enseguida.
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                if ((int)response.StatusCode >= 500)
                {
                    var status = (int)response.StatusCode;
                    response.Dispose();

                    throw ExternalServiceUnavailableException.BadGateway(ServiceName, status);
                }

                return response;
            }

            if (attempt >= options.MaxRetries)
            {
                response.Dispose();

                throw ExternalServiceUnavailableException.BadGateway(
                    ServiceName, (int)HttpStatusCode.ServiceUnavailable);
            }

            var delay = GetRetryDelay(response, attempt, _throttle.MinInterval);

            _logger.LogWarning(
                "MusicBrainz respondio 503 en {Uri}. Reintento {Attempt}/{Max} en {Delay}.",
                request.RequestUri, attempt + 1, options.MaxRetries, delay);

            response.Dispose();

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Backoff exponencial, salvo que el servidor indique un Retry-After.</summary>
    private static TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt, TimeSpan minInterval)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta)
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
