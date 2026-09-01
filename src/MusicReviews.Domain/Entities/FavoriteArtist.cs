namespace MusicReviews.Domain.Entities;

/// <summary>
/// Tabla puente entre usuario y artista favorito. Clave primaria compuesta (UserId, ArtistId):
/// no hace falta un Id sustituto y la PK ya garantiza que no se pueda favoritear dos veces.
/// </summary>
public class FavoriteArtist
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
