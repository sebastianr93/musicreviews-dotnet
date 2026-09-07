namespace MusicReviews.Application.Home.Dtos;

/// <summary>
/// Album tal como se muestra en el home: portada grande, artista y las estadisticas
/// que justifican que este ahi.
/// </summary>
/// <param name="AverageScore">Null mientras nadie lo haya puntuado.</param>
/// <param name="ActivityScore">
/// Puntaje de actividad reciente. Solo lo llena la seccion de populares; en el resto
/// va en cero porque ahi no se calculo, no porque no haya habido actividad.
/// </param>
public sealed record HomeAlbumDto(
    int Id,
    string MusicBrainzId,
    string Title,
    string? CoverArtUrl,
    string ArtistName,
    string ArtistMusicBrainzId,
    DateOnly? ReleaseDate,
    int ReviewCount,
    double? AverageScore,
    int ActivityScore = 0);
