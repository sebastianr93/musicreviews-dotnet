using MusicReviews.Domain.Enums;

namespace MusicReviews.Domain.Entities;

/// <summary>
/// Voto de un usuario sobre una review o un comentario.
/// </summary>
/// <remarks>
/// Es una relacion polimorfica: (<see cref="TargetType"/>, <see cref="TargetId"/>) apunta a
/// dos tablas distintas, asi que no hay FK real hacia el target. La contrapartida es que
/// la integridad referencial la sostiene la aplicacion (al borrar una review o un comentario
/// hay que borrar sus likes) en vez de la base. Se elige asi para tener una sola tabla de
/// votos con una sola query de conteo, en vez de ReviewLike + CommentLike duplicadas.
/// El indice unico (UserId, TargetType, TargetId) impide votar dos veces lo mismo.
/// </remarks>
public class Like
{
    public int Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public LikeTargetType TargetType { get; set; }

    public int TargetId { get; set; }

    /// <summary>true = like, false = dislike.</summary>
    public bool IsLike { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
