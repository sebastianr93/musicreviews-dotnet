using MusicReviews.Application.Auth.Dtos;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Application.Auth;

/// <summary>
/// Casos de uso de autenticacion. La implementacion vive en Infrastructure porque
/// necesita UserManager y el DbContext.
/// </summary>
public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rota el refresh token: revoca el presentado y emite un par nuevo.
    /// Si el token presentado ya estaba revocado, revoca todos los tokens activos
    /// del usuario (posible robo) y falla.
    /// </summary>
    Task<Result<AuthResponse>> RefreshAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Revoca un refresh token puntual (logout).</summary>
    Task<Result> RevokeAsync(
        RevokeTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
