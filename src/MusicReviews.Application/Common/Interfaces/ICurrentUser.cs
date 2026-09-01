namespace MusicReviews.Application.Common.Interfaces;

/// <summary>
/// Acceso al usuario autenticado de la request en curso.
/// La implementacion vive en la capa Api (es la unica que conoce HttpContext);
/// los servicios de negocio dependen solo de esta interfaz.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Id del usuario autenticado, o null si la request es anonima.</summary>
    Guid? UserId { get; }

    string? UserName { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}
