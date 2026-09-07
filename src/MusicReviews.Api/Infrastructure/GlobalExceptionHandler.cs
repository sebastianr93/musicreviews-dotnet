using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MusicReviews.Application.Common.Exceptions;

namespace MusicReviews.Api.Infrastructure;

/// <summary>
/// Convierte cualquier excepcion no manejada en una respuesta ProblemDetails uniforme.
/// Se registra con <c>AddExceptionHandler</c> y actua detras de <c>UseExceptionHandler</c>.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            ExternalServiceUnavailableException unavailable
                => HandleUnavailable(httpContext, unavailable),

            // El cliente corto la conexion (cerro la pestaña, cancelo la busqueda).
            // No hay a quien responderle y no es un error de la aplicacion.
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested
                => null,

            _ => HandleUnexpected(httpContext, exception)
        };

        if (problemDetails is null)
        {
            return true;
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    /// <summary>
    /// Un tercero caido no es un bug nuestro: 503 con Retry-After y un mensaje que
    /// le dice al usuario que reintentar sirve. Se loguea como Warning, no Error,
    /// porque un 500 en el log tiene que significar "hay algo que arreglar".
    /// </summary>
    private ProblemDetails HandleUnavailable(HttpContext context, ExternalServiceUnavailableException exception)
    {
        _logger.LogWarning(
            exception,
            "{Service} no disponible procesando {Method} {Path}",
            exception.ServiceName,
            context.Request.Method,
            context.Request.Path);

        var retryAfter = exception.RetryAfter ?? TimeSpan.FromSeconds(5);
        context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Servicio no disponible temporalmente.",
            Detail = exception.Message,
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.4"
        };

        problem.Extensions["code"] = "external_service_unavailable";
        problem.Extensions["service"] = exception.ServiceName;

        return problem;
    }

    private ProblemDetails HandleUnexpected(HttpContext context, Exception exception)
    {
        _logger.LogError(
            exception,
            "Excepcion no manejada procesando {Method} {Path}",
            context.Request.Method,
            context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ocurrió un error inesperado.",
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1"
        };

        problem.Extensions["code"] = "unexpected_error";

        // En desarrollo se expone el mensaje y el tipo, no el ToString() completo:
        // el stack trace entero en el cuerpo de la respuesta llena la pantalla del
        // navegador y no aporta nada que no este ya en el log, con mejor formato.
        if (_environment.IsDevelopment())
        {
            problem.Detail = exception.Message;
            problem.Extensions["exceptionType"] = exception.GetType().FullName;
        }

        return problem;
    }
}
