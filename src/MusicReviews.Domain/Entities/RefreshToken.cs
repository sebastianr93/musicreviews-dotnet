namespace MusicReviews.Domain.Entities;

/// <summary>
/// Refresh token emitido a un usuario.
/// </summary>
/// <remarks>
/// No se guarda el token en claro: se persiste su hash SHA-256. Si la base se filtra,
/// los tokens robados no sirven para pedir un access token nuevo. Es el mismo criterio
/// que se aplica a las contrasenias, y la razon por la que el valor en claro solo existe
/// una vez, en la respuesta HTTP que lo emite.
///
/// Los tokens rotan: cada refresh revoca el token usado y emite uno nuevo, encadenado por
/// <see cref="ReplacedByTokenHash"/>. Si llega un token ya revocado, es senial de robo
/// (alguien esta reusando un token viejo) y se revoca la familia completa del usuario.
/// </remarks>
public class RefreshToken
{
    public const int TokenHashLength = 64; // SHA-256 en hexadecimal
    public const int IpMaxLength = 45;     // IPv6 en su forma mas larga

    public int Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>Hash SHA-256 (hex) del token. Nunca se guarda el valor en claro.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedByIp { get; set; }

    /// <summary>Hash del token que reemplazo a este durante una rotacion.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);
}
