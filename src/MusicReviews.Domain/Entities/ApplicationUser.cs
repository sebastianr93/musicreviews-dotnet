using Microsoft.AspNetCore.Identity;

namespace MusicReviews.Domain.Entities;

/// <summary>
/// Usuario de la aplicacion. Hereda de <see cref="IdentityUser{TKey}"/>, que ya aporta
/// Id, UserName, NormalizedUserName, Email, PasswordHash, SecurityStamp, etc.
/// Aca solo se agregan los campos propios del perfil publico.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public const int BioMaxLength = 500;
    public const int AvatarUrlMaxLength = 2048;

    /// <summary>Texto libre del perfil publico. Opcional.</summary>
    public string? Bio { get; set; }

    /// <summary>URL de la imagen de perfil. Opcional.</summary>
    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navegaciones
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<Like> Likes { get; set; } = [];
    public ICollection<FavoriteArtist> FavoriteArtists { get; set; } = [];
}
