using MusicReviews.Application.Reviews.Dtos;

namespace MusicReviews.Application.Comments.Dtos;

/// <summary>
/// Nodo del arbol de comentarios.
/// </summary>
/// <remarks>
/// Es una clase y no un record posicional a proposito: <see cref="Replies"/> se llena
/// durante el armado del arbol, despues de materializar la consulta. Todo lo demas es
/// <c>init</c>, asi que el unico estado mutable es la lista de hijos.
///
/// Cuando <see cref="IsDeleted"/> es true, <see cref="Text"/> y <see cref="Author"/>
/// vienen en null: el nodo sigue en el arbol para sostener sus respuestas, pero no
/// expone el contenido borrado.
/// </remarks>
public sealed class CommentNodeDto
{
    public int Id { get; init; }

    public int? ParentCommentId { get; init; }

    public int Depth { get; init; }

    public string? Text { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public AuthorDto? Author { get; init; }

    public int LikeCount { get; init; }

    public int DislikeCount { get; init; }

    /// <summary>true = like, false = dislike, null = sin voto o request anonima.</summary>
    public bool? CurrentUserVote { get; init; }

    /// <summary>Respuestas directas, en orden cronologico.</summary>
    public List<CommentNodeDto> Replies { get; } = [];

    public int ReplyCount => Replies.Count;
}

public sealed record CreateCommentRequest(
    string Text,
    int? ParentCommentId);

public sealed record UpdateCommentRequest(string Text);

/// <summary>Criterio de orden de los comentarios de primer nivel. Las respuestas van siempre cronologicas.</summary>
public enum CommentSortOrder
{
    /// <summary>Mas antiguos primero. Por defecto: es como se lee un hilo.</summary>
    Oldest = 0,
    Newest = 1,
    MostLiked = 2
}
