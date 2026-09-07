using MusicReviews.Application.Auth.Dtos;
using MusicReviews.Application.Users.Dtos;
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

    /// <summary>
    /// Cambia la contraseña del usuario autenticado y <b>revoca todas sus sesiones</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pide la contraseña actual aunque la sesion ya este abierta: si alguien deja el
    /// navegador desbloqueado, no deberia poder quedarse con la cuenta con dos clicks.
    /// </para>
    /// <para>
    /// Revocar los refresh tokens es el punto del ejercicio. Cambiar la contraseña
    /// normalmente se hace <i>porque</i> se sospecha que alguien mas entro; si las
    /// sesiones viejas siguieran renovandose, el cambio no serviria para nada. El
    /// usuario tiene que volver a entrar en sus otros dispositivos, que es exactamente
    /// lo que se busca.
    /// </para>
    /// </remarks>
    Task<Result> ChangePasswordAsync(
        ChangePasswordRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Revoca un refresh token puntual (logout).</summary>
    Task<Result> RevokeAsync(
        RevokeTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
