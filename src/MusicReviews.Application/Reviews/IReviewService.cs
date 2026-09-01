using MusicReviews.Application.Common.Models;
using MusicReviews.Application.Common.Results;
using MusicReviews.Application.Reviews.Dtos;

namespace MusicReviews.Application.Reviews;

public interface IReviewService
{
    Task<Result<ReviewDto>> GetByIdAsync(
        int reviewId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ReviewDto>>> GetByAlbumAsync(
        string albumMusicBrainzId,
        PageRequest page,
        ReviewSortOrder sort = ReviewSortOrder.Newest,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ReviewDto>>> GetByUserAsync(
        string userName,
        PageRequest page,
        ReviewSortOrder sort = ReviewSortOrder.Newest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea la review del usuario autenticado. Si el album todavia no estaba en
    /// el catalogo local, lo trae de MusicBrainz y lo persiste primero.
    /// Falla con Conflict si el usuario ya reseño ese album.
    /// </summary>
    Task<Result<ReviewDto>> CreateAsync(
        CreateReviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Edita una review. Solo el autor.</summary>
    Task<Result<ReviewDto>> UpdateAsync(
        int reviewId,
        UpdateReviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Borra una review. El autor, o un administrador moderando.</summary>
    Task<Result> DeleteAsync(
        int reviewId,
        CancellationToken cancellationToken = default);
}
