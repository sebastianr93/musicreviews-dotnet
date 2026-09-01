namespace MusicReviews.Domain.Entities;

/// <summary>
/// Comentario sobre una review. <see cref="ParentCommentId"/> es la auto-referencia
/// que arma el hilo anidado: null = comentario de primer nivel, con valor = respuesta.
/// </summary>
/// <remarks>
/// Tres decisiones que importan para el armado del arbol (ver seccion 5 de la spec):
/// <list type="bullet">
/// <item><description>
/// Todo comentario guarda <see cref="ReviewId"/>, incluso las respuestas anidadas a
/// cualquier profundidad. Esa denormalizacion es lo que permite traer el hilo completo
/// con un unico <c>WHERE ReviewId = X</c>, sin recursion en la base.
/// </description></item>
/// <item><description>
/// <see cref="Depth"/> se calcula al insertar (<c>padre.Depth + 1</c>) y no se recalcula:
/// permite aplicar el limite de anidamiento sin subir por la cadena de padres, y deja
/// preparada la paginacion por niveles.
/// </description></item>
/// <item><description>
/// El borrado es logico (<see cref="IsDeleted"/>), no fisico. Borrar un nodo intermedio
/// de forma fisica obligaria a elegir entre romper el hilo o cascadear las respuestas
/// de otros usuarios. Con soft delete el nodo sobrevive como "[eliminado]" y el arbol
/// se mantiene intacto.
/// </description></item>
/// </list>
/// </remarks>
public class Comment
{
    public const int TextMaxLength = 5_000;

    /// <summary>
    /// Profundidad maxima de anidamiento. No es una restriccion tecnica: el armado del
    /// arbol es iterativo y soporta cualquier profundidad. Es un limite de producto,
    /// porque a partir de cierto nivel un hilo deja de ser legible.
    /// </summary>
    public const int MaxDepth = 10;

    public int Id { get; set; }

    public int ReviewId { get; set; }
    public Review Review { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>Null para comentarios de primer nivel.</summary>
    public int? ParentCommentId { get; set; }
    public Comment? ParentComment { get; set; }

    /// <summary>0 para comentarios de primer nivel; <c>padre.Depth + 1</c> para respuestas.</summary>
    public int Depth { get; set; }

    public string Text { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Borrado logico: el nodo se conserva para no romper el hilo de respuestas.</summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // Navegaciones
    public ICollection<Comment> Replies { get; set; } = [];
}
