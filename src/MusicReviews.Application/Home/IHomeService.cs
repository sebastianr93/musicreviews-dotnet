using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Home.Dtos;

namespace MusicReviews.Application.Home;

/// <summary>
/// Las secciones del home que se arman con datos propios.
/// </summary>
/// <remarks>
/// La tercera seccion del home —la actividad de a quienes seguis— no esta aca: ya la
/// resuelve <c>IActivityService.GetFollowingFeedAsync</c> y duplicarla para que las tres
/// vivieran juntas seria mantener dos caminos para el mismo feed.
/// </remarks>
public interface IHomeService
{
    /// <summary>
    /// Albumes con mas movimiento en los ultimos dias: lo que se esta reseniando,
    /// comentando y votando ahora.
    /// </summary>
    /// <param name="windowDays">Ventana de actividad, en dias.</param>
    Task<Result<PagedResult<HomeAlbumDto>>> GetPopularAsync(
        PageRequest page,
        int windowDays = HomeDefaults.PopularWindowDays,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Albumes del catalogo local para descubrir. Excluye los que el usuario
    /// autenticado ya reseñó: recomendarle lo que ya escucho y puntuo no descubre nada.
    /// </summary>
    Task<Result<PagedResult<HomeAlbumDto>>> GetExploreAsync(
        PageRequest page,
        CancellationToken cancellationToken = default);
}

public static class HomeDefaults
{
    /// <summary>
    /// Ventana de la seccion de populares. Treinta dias es el equilibrio entre "esto
    /// esta pasando ahora" y tener suficiente material como para que la seccion no
    /// aparezca vacia en un sitio con poco trafico.
    /// </summary>
    public const int PopularWindowDays = 30;

    public const int MinWindowDays = 1;
    public const int MaxWindowDays = 365;

    /// <summary>
    /// Peso de cada tipo de actividad. Escribir una reseña es una señal de interes
    /// mucho mas fuerte que hacer un click, y sin pesos un album con cincuenta votos
    /// desplazaria a uno con diez reseñas escritas.
    /// </summary>
    public const int ReviewWeight = 3;
    public const int CommentWeight = 2;
    public const int VoteWeight = 1;
}
