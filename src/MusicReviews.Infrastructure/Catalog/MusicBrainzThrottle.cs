using Microsoft.Extensions.Options;

namespace MusicReviews.Infrastructure.Catalog;

/// <summary>
/// Cola global de salida hacia MusicBrainz: garantiza que no se emita mas de un
/// request por intervalo configurado, para todo el proceso.
/// </summary>
/// <remarks>
/// El limite de MusicBrainz (1 req/seg para un cliente identificado por User-Agent)
/// es por aplicacion, no por usuario: si dos requests HTTP entrantes disparan dos
/// llamadas en paralelo, se viola igual. Por eso el estado vive en un singleton y
/// no en el <see cref="MusicBrainzThrottlingHandler"/>, que HttpClientFactory recicla
/// cada pocos minutos.
///
/// Es una solucion de un solo nodo: si la Api escalara horizontalmente, cada instancia
/// tendria su propia cola. A esa altura corresponde un throttle distribuido (Redis) o,
/// mejor, una replica local de los data dumps publicos de MusicBrainz.
/// </remarks>
public sealed class MusicBrainzThrottle : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IOptionsMonitor<MusicBrainzOptions> _options;
    private readonly TimeProvider _timeProvider;

    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public MusicBrainzThrottle(IOptionsMonitor<MusicBrainzOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(_options.CurrentValue.MinIntervalMilliseconds);

    /// <summary>Espera hasta que sea legal emitir el proximo request y reserva ese turno.</summary>
    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var minInterval = MinInterval;
            var elapsed = _timeProvider.GetUtcNow() - _lastRequestAt;

            if (elapsed < minInterval)
            {
                await Task.Delay(minInterval - elapsed, cancellationToken);
            }

            _lastRequestAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
