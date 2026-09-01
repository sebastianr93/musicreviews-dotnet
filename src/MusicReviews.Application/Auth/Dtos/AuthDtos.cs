namespace MusicReviews.Application.Auth.Dtos;

/// <summary>Datos de alta de una cuenta nueva.</summary>
public sealed record RegisterRequest(
    string UserName,
    string Email,
    string Password);

/// <summary>Credenciales de acceso. <paramref name="UserNameOrEmail"/> acepta cualquiera de los dos.</summary>
public sealed record LoginRequest(
    string UserNameOrEmail,
    string Password);

/// <summary>Pedido de renovacion de tokens.</summary>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>Pedido de revocacion explicita (logout).</summary>
public sealed record RevokeTokenRequest(string RefreshToken);

/// <summary>
/// Par de tokens emitido tras un login o un refresh.
/// El refresh token viaja en claro una unica vez, en esta respuesta: en la base solo queda su hash.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserSummaryDto User);

/// <summary>Vista minima del usuario que acompania a la respuesta de autenticacion.</summary>
public sealed record UserSummaryDto(
    Guid Id,
    string UserName,
    string Email,
    string? AvatarUrl,
    IReadOnlyList<string> Roles);
