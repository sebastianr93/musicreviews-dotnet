using MusicReviews.Application.Activity.Dtos;
using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Application.Activity;

public interface IActivityService
{
    /// <summary>Timeline de un usuario: lo que publico, comento y voto.</summary>
    Task<Result<CursorPage<ActivityEntryDto>>> GetUserActivityAsync(
        string userName,
        CursorRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Timeline combinado de los usuarios a los que sigue el usuario autenticado.
    /// Devuelve una pagina vacia si todavia no sigue a nadie.
    /// </summary>
    Task<Result<CursorPage<ActivityEntryDto>>> GetFollowingFeedAsync(
        CursorRequest request,
        CancellationToken cancellationToken = default);
}
