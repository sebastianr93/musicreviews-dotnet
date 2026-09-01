namespace MusicReviews.Domain.Entities;

/// <summary>
/// Review de un usuario sobre un album. Un usuario solo puede tener una review por album:
/// eso se garantiza con un indice unico compuesto (UserId, AlbumId) en la base, no solo en codigo.
/// </summary>
public class Review
{
    public const int MinScore = 0;
    public const int MaxScore = 100;
    public const int TextMaxLength = 10_000;

    public int Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int AlbumId { get; set; }
    public Album Album { get; set; } = null!;

    /// <summary>Puntaje de 0 a 100. Validado en la capa Application y por CHECK constraint en la base.</summary>
    public int Score { get; set; }

    public string Text { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null mientras la review no haya sido editada.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navegaciones
    public ICollection<Comment> Comments { get; set; } = [];
}
