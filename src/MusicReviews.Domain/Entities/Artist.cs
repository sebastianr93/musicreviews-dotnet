namespace MusicReviews.Domain.Entities;

/// <summary>
/// Artista del catalogo. Se cachea localmente la primera vez que se consulta a MusicBrainz;
/// <see cref="MusicBrainzId"/> es la clave natural que evita duplicados entre sincronizaciones.
/// </summary>
public class Artist
{
    public const int MusicBrainzIdLength = 36;
    public const int NameMaxLength = 400;
    public const int DisambiguationMaxLength = 400;
    public const int UrlMaxLength = 2048;

    public int Id { get; set; }

    /// <summary>MBID del artista (GUID de 36 caracteres). Clave natural, unica e indexada.</summary>
    public string MusicBrainzId { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Texto que MusicBrainz usa para desambiguar homonimos (ej. "UK punk band").</summary>
    public string? Disambiguation { get; set; }

    /// <summary>Codigo ISO del pais de origen segun MusicBrainz (ej. "AR", "GB").</summary>
    public string? Country { get; set; }

    /// <summary>Tipo declarado por MusicBrainz: Person, Group, Orchestra, etc.</summary>
    public string? Type { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Momento en que se trajo/refresco desde MusicBrainz. Base para invalidar la cache.</summary>
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navegaciones
    public ICollection<Album> Albums { get; set; } = [];
    public ICollection<FavoriteArtist> FavoritedBy { get; set; } = [];
}
