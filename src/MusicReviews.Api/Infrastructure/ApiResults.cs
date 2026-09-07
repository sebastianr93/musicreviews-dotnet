using Microsoft.AspNetCore.Mvc;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Api.Infrastructure;

/// <summary>
/// Traduce los errores de la capa Application a respuestas HTTP.
/// Es el unico lugar donde un <see cref="ErrorType"/> se convierte en un status code:
/// la logica de negocio nunca decide codigos HTTP.
/// </summary>
public static class ApiResults
{
    public static IActionResult Problem(this ControllerBase controller, Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        // 503 sin Retry-After invita al cliente a reintentar en bucle sobre un
        // servicio que ya esta en problemas.
        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            controller.Response.Headers.RetryAfter = "5";
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = TitleFor(error.Type),
            Detail = error.Message,
            Instance = controller.HttpContext.Request.Path
        };

        // Codigo estable y legible por maquina, para que el cliente no tenga que
        // parsear el mensaje en castellano.
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;

        return controller.StatusCode(statusCode, problem);
    }

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "La solicitud no es válida.",
        ErrorType.Unauthorized => "No autenticado.",
        ErrorType.Forbidden => "No tenés permiso para hacer esto.",
        ErrorType.NotFound => "El recurso no existe.",
        ErrorType.Conflict => "El estado actual no permite la operación.",
        ErrorType.Unavailable => "Servicio no disponible temporalmente.",
        _ => "Ocurrió un error inesperado."
    };
}
