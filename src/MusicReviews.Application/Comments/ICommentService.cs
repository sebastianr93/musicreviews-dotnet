using MusicReviews.Application.Comments.Dtos;
using MusicReviews.Application.Common.Results;

namespace MusicReviews.Application.Comments;

public interface ICommentService
{
    /// <summary>
    /// Trae el hilo completo de una review: UNA consulta plana a la base y el arbol
    /// armado en memoria. Ver <see cref="CommentTreeBuilder"/>.
    /// </summary>
    Task<Result<IReadOnlyList<CommentNodeDto>>> GetTreeAsync(
        int reviewId,
        CommentSortOrder sort = CommentSortOrder.Oldest,
        CancellationToken cancellationToken = default);

    Task<Result<CommentNodeDto>> GetByIdAsync(
        int commentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica un comentario. Si <c>ParentCommentId</c> viene, tiene que pertenecer a
    /// la misma review, no estar borrado y no superar la profundidad maxima.
    /// </summary>
    Task<Result<CommentNodeDto>> CreateAsync(
        int reviewId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Edita un comentario. Solo el autor.</summary>
    Task<Result<CommentNodeDto>> UpdateAsync(
        int commentId,
        UpdateCommentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Borrado logico. El autor, o un administrador moderando.
    /// El nodo se conserva para no romper el hilo de respuestas.
    /// </summary>
    Task<Result> DeleteAsync(
        int commentId,
        CancellationToken cancellationToken = default);
}
