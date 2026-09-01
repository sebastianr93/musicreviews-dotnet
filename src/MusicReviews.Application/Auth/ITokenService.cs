using MusicReviews.Domain.Entities;

namespace MusicReviews.Application.Auth;

/// <summary>Token de acceso emitido, con su vencimiento.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Refresh token recien generado. <paramref name="Value"/> es el unico momento
/// en que existe en claro; lo que se persiste es <paramref name="Hash"/>.
/// </summary>
public sealed record GeneratedRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt);

/// <summary>Emision y hasheo de tokens.</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    GeneratedRefreshToken CreateRefreshToken();

    /// <summary>Hash SHA-256 en hexadecimal, para buscar un token recibido contra lo guardado.</summary>
    string HashRefreshToken(string refreshToken);
}
