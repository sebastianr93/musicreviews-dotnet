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
    string? Type,
    int MatchScore = 0);

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

/// <summary>
/// Album en un listado. <c>MatchScore</c> es la relevancia 0-100 que devuelve la
/// busqueda de MusicBrainz y <c>ReleaseCount</c> la cantidad de ediciones publicadas;
/// los dos alimentan el orden de los resultados (ver <c>AlbumSearchRanking</c>) y
/// valen la pena en la respuesta para que el cliente pueda mostrar por que un
/// resultado esta primero.
/// </summary>
public sealed record AlbumSummaryDto(
    int? Id,
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    DateOnly? ReleaseDate,
    string? PrimaryType,
    string ArtistName,
    string ArtistMusicBrainzId,
    int MatchScore = 0,
    int ReleaseCount = 0);

/// <summary>
/// Cancion en una busqueda, ya agrupada: representa a todas las grabaciones que
/// MusicBrainz tiene del mismo tema.
/// </summary>
/// <param name="MusicBrainzId">Grabacion representante del grupo.</param>
/// <param name="AlbumMusicBrainzId">
/// Release-group al que lleva el resultado. Es el destino del enlace: en esta aplicacion
/// se reseña el album, no la cancion, asi que una cancion es una forma de llegar a el.
/// Null cuando la grabacion no esta asociada a ninguna edicion.
/// </param>
/// <param name="VersionCount">
/// Cuantas grabaciones se colapsaron en esta. Cumple para las canciones el mismo papel
/// que <c>ReleaseCount</c> para los albumes: es la unica señal de popularidad
/// disponible, porque solo un tema muy difundido acumula decenas de versiones.
/// </param>
public sealed record SongSearchItemDto(
    string MusicBrainzId,
    string Title,
    string ArtistName,
    string ArtistMusicBrainzId,
    int? LengthMilliseconds,
    string? AlbumTitle,
    string? AlbumMusicBrainzId,
    string? CoverArtUrl,
    DateOnly? ReleaseDate,
    int VersionCount = 1,
    int MatchScore = 0);

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
