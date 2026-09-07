namespace MusicReviews.Application.Common.Exceptions;

/// <summary>
/// Un servicio externo del que depende la aplicacion no respondio: se cayo, tardo mas
/// de lo aceptable, o devolvio un error de servidor.
/// </summary>
/// <remarks>
/// Existe para distinguir "el tercero fallo" de "nuestro codigo tiene un bug", que es
/// una diferencia que importa en tres lugares:
/// <list type="bullet">
/// <item><description>
/// El status HTTP: un timeout de MusicBrainz es un 503 con Retry-After, no un 500.
/// Un 500 le dice al cliente "esto esta roto"; un 503 le dice "volve a intentar".
/// </description></item>
/// <item><description>
/// El log: una excepcion de transporte no necesita el stack trace completo en nivel
/// Error todas las veces, y mezclada con las excepciones reales tapa los bugs de verdad.
/// </description></item>
/// <item><description>
/// El mensaje al usuario: "el catalogo de musica no responde en este momento" es
/// accionable; "Ocurrio un error inesperado" no.
/// </description></item>
/// </list>
/// </remarks>
public sealed class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(
        string serviceName,
        string message,
        Exception? innerException = null,
        TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        ServiceName = serviceName;
        RetryAfter = retryAfter;
    }

    /// <summary>Servicio que fallo, para el log y para el mensaje al cliente.</summary>
    public string ServiceName { get; }

    /// <summary>Cuanto conviene esperar antes de reintentar, si se sabe.</summary>
    public TimeSpan? RetryAfter { get; }

    public static ExternalServiceUnavailableException Timeout(
        string serviceName,
        TimeSpan elapsed,
        Exception? innerException = null) =>
        new(serviceName,
            $"{serviceName} no respondió dentro de los {elapsed.TotalSeconds:0.#} segundos.",
            innerException,
            TimeSpan.FromSeconds(5));

    public static ExternalServiceUnavailableException Transport(
        string serviceName,
        Exception innerException) =>
        new(serviceName,
            $"No se pudo conectar con {serviceName}.",
            innerException,
            TimeSpan.FromSeconds(5));

    public static ExternalServiceUnavailableException BadGateway(
        string serviceName,
        int statusCode) =>
        new(serviceName,
            $"{serviceName} respondió con un error {statusCode}.",
            retryAfter: TimeSpan.FromSeconds(10));
}
