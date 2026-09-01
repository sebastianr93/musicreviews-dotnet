namespace MusicReviews.Application.Catalog.Dtos;

/// <summary>
/// Artista tal como lo devuelve una busqueda. Puede venir de la cache local
/// o directamente de MusicBrainz, por eso el Id local es nullable: si todavia
/// no se cacheo, solo existe el MBID.
/// </summary>
public sealed record ArtistSearchItemDto(
    int? Id,
    string MusicBrainzId,
    string Name,
    string? Disambiguation,
    string? Country,
    string? Type);

/// <summary>Detalle de artista, ya cacheado localmente.</summary>
public sealed record ArtistDetailDto(
    int Id,
    string MusicBrainzId,
    string Name,
    string? Disambiguation,
    string? Country,
    string? Type,
    string? ImageUrl,
    int FavoriteCount,
    DateTimeOffset CachedAt);

/// <summary>Album en un listado.</summary>
public sealed record AlbumSummaryDto(
    int? Id,
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    DateOnly? ReleaseDate,
    string? PrimaryType,
    string ArtistName,
    string ArtistMusicBrainzId);

/// <summary>
/// Detalle de album, con las estadisticas locales de reviews.
/// <paramref name="AverageScore"/> es null cuando todavia no hay ninguna.
/// </summary>
public sealed record AlbumDetailDto(
    int Id,
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    DateOnly? ReleaseDate,
    string? PrimaryType,
    ArtistSearchItemDto Artist,
    int ReviewCount,
    double? AverageScore,
    DateTimeOffset CachedAt);
