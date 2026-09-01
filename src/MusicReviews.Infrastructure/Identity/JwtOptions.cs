using System.ComponentModel.DataAnnotations;

namespace MusicReviews.Infrastructure.Identity;

/// <summary>
/// Configuracion de emision y validacion de JWT. Se enlaza a la seccion "Jwt" y se valida
/// al arrancar: si falta la clave o es corta, la aplicacion no levanta en vez de emitir
/// tokens inseguros en silencio.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Longitud minima de la clave para HMAC-SHA256 (256 bits).</summary>
    public const int MinimumSigningKeyLength = 32;

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumSigningKeyLength,
        ErrorMessage = "La clave de firma tiene que tener al menos 32 caracteres para HMAC-SHA256.")]
    public string SigningKey { get; set; } = null!;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;
}
