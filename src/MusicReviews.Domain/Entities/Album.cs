namespace MusicReviews.Domain.Entities;

/// <summary>
/// Album del catalogo, cacheado desde MusicBrainz (release-group) + Cover Art Archive.
/// </summary>
public class Album
{
    public const int MusicBrainzIdLength = 36;
    public const int TitleMaxLength = 400;
    public const int UrlMaxLength = 2048;
    public const int PrimaryTypeMaxLength = 50;

    public int Id { get; set; }

    /// <summary>MBID del release-group. Clave natural, unica e indexada.</summary>
    public string MusicBrainzId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    public string? CoverArtUrl { get; set; }

    /// <summary>
    /// Fecha de lanzamiento. Nullable porque MusicBrainz devuelve fechas parciales
    /// (a veces solo el anio); en ese caso se normaliza al 1 de enero y se conserva
    /// <see cref="ReleaseDatePrecision"/> para saber cuanto de la fecha es real.
    /// </summary>
    public DateOnly? ReleaseDate { get; set; }

    /// <summary>Precision de <see cref="ReleaseDate"/>: 0 = desconocida, 1 = anio, 2 = mes, 3 = dia.</summary>
    public int ReleaseDatePrecision { get; set; }

    /// <summary>Tipo primario segun MusicBrainz: Album, EP, Single, Live, etc.</summary>
    public string? PrimaryType { get; set; }

    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navegaciones
    public ICollection<Review> Reviews { get; set; } = [];
}
