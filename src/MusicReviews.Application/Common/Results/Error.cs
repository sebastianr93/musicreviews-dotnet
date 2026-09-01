namespace MusicReviews.Application.Common.Results;

/// <summary>
/// Categoria del error. La capa Api la traduce a un status HTTP; la capa Application
/// no conoce codigos HTTP.
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Failure
}

/// <summary>
/// Error de negocio esperado. Se devuelve como valor, no se lanza como excepcion:
/// "el email ya esta en uso" es un resultado previsible del flujo, no una falla.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
}
