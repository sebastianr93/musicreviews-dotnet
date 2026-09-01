using MusicReviews.Application.Comments.Dtos;

namespace MusicReviews.Application.Comments;

/// <summary>
/// Arma el arbol de comentarios en memoria a partir de la lista plana que devuelve
/// una unica consulta a la base.
/// </summary>
/// <remarks>
/// <para>
/// <b>El problema que resuelve.</b> La forma ingenua de traer un hilo anidado es
/// recursiva: traer los comentarios de primer nivel y, por cada uno, consultar sus
/// respuestas; por cada respuesta, las suyas. Eso es exactamente el patron N+1: un
/// hilo de 200 comentarios dispara 201 consultas, y la cantidad depende de la forma
/// del arbol, no del volumen de datos.
/// </para>
/// <para>
/// <b>La forma correcta.</b> Todo comentario guarda su <c>ReviewId</c>, incluso las
/// respuestas anidadas a cualquier profundidad. Eso permite traer el hilo entero con
/// un solo <c>WHERE ReviewId = X</c> y reconstruir la jerarquia aca, en memoria, con
/// un diccionario indexado por Id. El costo es O(n) en tiempo y memoria, con dos
/// pasadas sobre la lista y ninguna consulta adicional, sea cual sea la profundidad.
/// </para>
/// <para>
/// <b>Sin recursion.</b> El armado es iterativo. Una implementacion recursiva
/// funcionaria igual de bien en un hilo normal, pero un hilo patologicamente profundo
/// (o construido a proposito) desbordaria la pila. Aca la profundidad del arbol no
/// consume pila.
/// </para>
/// <para>
/// <b>Por que no puede haber ciclos.</b> <c>Comment.Id</c> es una secuencia: un
/// comentario siempre se crea despues de su padre, asi que su Id es mayor. La arista
/// se crea solo cuando <c>ParentCommentId &lt; Id</c>, lo que hace imposible construir
/// un ciclo aunque la base llegara corrupta o alguien editara filas a mano. Un nodo
/// que no cumple esa condicion se promueve a raiz: queda visible en la respuesta en
/// vez de desaparecer en silencio.
/// </para>
/// </remarks>
public static class CommentTreeBuilder
{
    /// <summary>
    /// Reconstruye la jerarquia. Devuelve los nodos de primer nivel; las respuestas
    /// quedan colgadas de <see cref="CommentNodeDto.Replies"/> en el orden en que
    /// venian en <paramref name="flatComments"/>.
    /// </summary>
    /// <param name="flatComments">
    /// Todos los comentarios de una review, en el orden en que deben quedar las
    /// respuestas (normalmente cronologico). No hace falta que esten ordenados por
    /// profundidad ni agrupados por padre.
    /// </param>
    /// <returns>
    /// Los nodos raiz. Se garantiza que cada elemento de la entrada aparece exactamente
    /// una vez en el arbol resultante: como raiz o como respuesta de otro nodo.
    /// </returns>
    public static IReadOnlyList<CommentNodeDto> Build(IReadOnlyList<CommentNodeDto> flatComments)
    {
        if (flatComments.Count == 0)
        {
            return [];
        }

        // Primera pasada: indice por Id. TryAdd descarta duplicados en vez de
        // pisarlos, para que un mismo comentario no pueda quedar en dos ramas.
        var byId = new Dictionary<int, CommentNodeDto>(flatComments.Count);
        var unique = new List<CommentNodeDto>(flatComments.Count);

        foreach (var comment in flatComments)
        {
            if (byId.TryAdd(comment.Id, comment))
            {
                unique.Add(comment);
            }
        }

        // Segunda pasada: cada nodo se cuelga de su padre, o pasa a ser raiz.
        var roots = new List<CommentNodeDto>();

        foreach (var comment in unique)
        {
            if (comment.ParentCommentId is not { } parentId)
            {
                roots.Add(comment);
                continue;
            }

            // Un padre con Id mayor o igual es imposible con datos sanos. Aceptarlo
            // permitiria construir un ciclo, y un ciclo en la respuesta seria una
            // recursion infinita al serializar.
            if (parentId >= comment.Id)
            {
                roots.Add(comment);
                continue;
            }

            if (byId.TryGetValue(parentId, out var parent))
            {
                parent.Replies.Add(comment);
            }
            else
            {
                // Huerfano: el padre no vino en el conjunto. Con la consulta completa
                // del hilo no deberia pasar, pero si alguna vez se pagina por niveles
                // si puede, y perder el comentario seria peor que mostrarlo suelto.
                roots.Add(comment);
            }
        }

        return roots;
    }

    /// <summary>
    /// Recorre el arbol en profundidad, sin recursion, y devuelve todos los nodos.
    /// Util para contar, para tests, y para aplicar operaciones a todo el hilo.
    /// </summary>
    public static IEnumerable<CommentNodeDto> Flatten(IReadOnlyList<CommentNodeDto> roots)
    {
        var stack = new Stack<CommentNodeDto>();

        // Al reves, para que el recorrido respete el orden de las raices.
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push(roots[i]);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            yield return node;

            for (var i = node.Replies.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Replies[i]);
            }
        }
    }

    /// <summary>Cantidad total de nodos del arbol, incluidas todas las respuestas.</summary>
    public static int CountNodes(IReadOnlyList<CommentNodeDto> roots) => Flatten(roots).Count();
}
