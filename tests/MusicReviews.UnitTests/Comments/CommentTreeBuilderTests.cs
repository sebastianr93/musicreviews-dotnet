using MusicReviews.Application.Comments;
using MusicReviews.Application.Comments.Dtos;

namespace MusicReviews.UnitTests.Comments;

/// <summary>
/// Tests del armado del arbol de comentarios.
/// </summary>
/// <remarks>
/// Es la logica mas propensa a bugs sutiles de todo el backend: los errores no rompen
/// nada de forma visible, simplemente hacen desaparecer comentarios de la respuesta o
/// los cuelgan de la rama equivocada. Por eso el conjunto cubre, ademas del camino
/// feliz, la clase entera de datos que la base no deberia producir nunca: huerfanos,
/// auto-referencias, ciclos, duplicados y entrada desordenada.
///
/// La invariante que atraviesa todo el archivo: <b>cada comentario de la entrada
/// aparece exactamente una vez en el arbol</b>. Ni duplicado ni perdido.
/// </remarks>
public class CommentTreeBuilderTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CommentNodeDto Node(
        int id,
        int? parentId = null,
        int depth = 0,
        int minutesAfterOrigin = 0,
        bool isDeleted = false) =>
        new()
        {
            Id = id,
            ParentCommentId = parentId,
            Depth = depth,
            Text = isDeleted ? null : $"comentario {id}",
            CreatedAt = Origin.AddMinutes(minutesAfterOrigin),
            IsDeleted = isDeleted
        };

    private static int[] IdsOf(IEnumerable<CommentNodeDto> nodes) => [.. nodes.Select(n => n.Id)];

    // ------------------------------------------------------------------
    // Casos base
    // ------------------------------------------------------------------

    [Fact]
    public void Build_ConListaVacia_DevuelveVacio()
    {
        var result = CommentTreeBuilder.Build([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_ConUnSoloComentario_LoDevuelveComoRaiz()
    {
        var result = CommentTreeBuilder.Build([Node(1)]);

        var root = Assert.Single(result);
        Assert.Equal(1, root.Id);
        Assert.Empty(root.Replies);
    }

    [Fact]
    public void Build_SoloComentariosDePrimerNivel_LosDevuelveTodosEnOrdenDeEntrada()
    {
        var flat = new[] { Node(1), Node(2), Node(3) };

        var result = CommentTreeBuilder.Build(flat);

        Assert.Equal([1, 2, 3], IdsOf(result));
        Assert.All(result, node => Assert.Empty(node.Replies));
    }

    // ------------------------------------------------------------------
    // Anidamiento
    // ------------------------------------------------------------------

    [Fact]
    public void Build_ConUnaRespuesta_LaCuelgaDelPadre()
    {
        var flat = new[] { Node(1), Node(2, parentId: 1, depth: 1) };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        var reply = Assert.Single(root.Replies);
        Assert.Equal(2, reply.Id);
    }

    [Fact]
    public void Build_ConTresNiveles_ArmaLaJerarquiaCompleta()
    {
        //  1
        //  └── 2
        //      └── 3
        var flat = new[]
        {
            Node(1),
            Node(2, parentId: 1, depth: 1),
            Node(3, parentId: 2, depth: 2)
        };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        var level1 = Assert.Single(root.Replies);
        var level2 = Assert.Single(level1.Replies);

        Assert.Equal(1, root.Id);
        Assert.Equal(2, level1.Id);
        Assert.Equal(3, level2.Id);
        Assert.Empty(level2.Replies);
    }

    [Fact]
    public void Build_ConVariasRespuestasAlMismoPadre_RespetaElOrdenDeEntrada()
    {
        // El servicio entrega la lista ordenada por CreatedAt: el builder no reordena,
        // solo preserva. Si reordenara, el orden cronologico del hilo se perderia.
        var flat = new[]
        {
            Node(1),
            Node(2, parentId: 1, depth: 1, minutesAfterOrigin: 1),
            Node(3, parentId: 1, depth: 1, minutesAfterOrigin: 2),
            Node(4, parentId: 1, depth: 1, minutesAfterOrigin: 3)
        };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        Assert.Equal([2, 3, 4], IdsOf(root.Replies));
    }

    [Fact]
    public void Build_ConArbolRamificado_CuelgaCadaNodoDeSuPadre()
    {
        //  1               10
        //  ├── 2           └── 11
        //  │   └── 4
        //  └── 3
        var flat = new[]
        {
            Node(1),
            Node(2, parentId: 1, depth: 1),
            Node(3, parentId: 1, depth: 1),
            Node(4, parentId: 2, depth: 2),
            Node(10),
            Node(11, parentId: 10, depth: 1)
        };

        var result = CommentTreeBuilder.Build(flat);

        Assert.Equal([1, 10], IdsOf(result));
        Assert.Equal([2, 3], IdsOf(result[0].Replies));
        Assert.Equal([4], IdsOf(result[0].Replies[0].Replies));
        Assert.Empty(result[0].Replies[1].Replies);
        Assert.Equal([11], IdsOf(result[1].Replies));
    }

    [Fact]
    public void Build_ConLaEntradaDesordenada_ArmaElArbolIgual()
    {
        // Una respuesta puede venir antes que su padre si el orden es por CreatedAt
        // y dos filas comparten timestamp. El indice por Id se construye en una pasada
        // previa justamente para que el orden de entrada no importe.
        var flat = new[]
        {
            Node(3, parentId: 2, depth: 2),
            Node(2, parentId: 1, depth: 1),
            Node(1)
        };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        Assert.Equal(1, root.Id);
        Assert.Equal(2, Assert.Single(root.Replies).Id);
        Assert.Equal(3, Assert.Single(root.Replies[0].Replies).Id);
    }

    // ------------------------------------------------------------------
    // Profundidad: sin recursion, sin desbordar la pila
    // ------------------------------------------------------------------

    [Fact]
    public void Build_ConUnaCadenaDeMilNiveles_NoDesbordaLaPilaYAnidaTodo()
    {
        // Una implementacion recursiva revienta con una cadena asi. El limite de
        // producto (MaxDepth) es del servicio; el builder no tiene ninguno.
        const int depth = 1_000;

        var flat = new List<CommentNodeDto> { Node(1) };

        for (var id = 2; id <= depth; id++)
        {
            flat.Add(Node(id, parentId: id - 1, depth: id - 1));
        }

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        Assert.Equal(depth, CommentTreeBuilder.CountNodes(result));

        // Baja la cadena entera para confirmar que quedo realmente anidada.
        var current = root;
        var visited = 1;

        while (current.Replies.Count > 0)
        {
            current = current.Replies[0];
            visited++;
        }

        Assert.Equal(depth, visited);
        Assert.Equal(depth, current.Id);
    }

    // ------------------------------------------------------------------
    // Datos que la base no deberia producir
    // ------------------------------------------------------------------

    [Fact]
    public void Build_ConUnHuerfano_LoPromueveARaizEnVezDePerderlo()
    {
        // El padre 99 no vino en el conjunto. Perder el comentario seria peor que
        // mostrarlo suelto.
        var flat = new[] { Node(1), Node(2, parentId: 99, depth: 1) };

        var result = CommentTreeBuilder.Build(flat);

        Assert.Equal([1, 2], IdsOf(result));
        Assert.Equal(2, CommentTreeBuilder.CountNodes(result));
    }

    [Fact]
    public void Build_ConAutoReferencia_LoPromueveARaiz()
    {
        var flat = new[] { Node(1, parentId: 1) };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        Assert.Equal(1, root.Id);
        Assert.Empty(root.Replies);
    }

    [Fact]
    public void Build_ConPadreDeIdMayor_LoPromueveARaiz()
    {
        // Imposible con datos sanos: el Id es una secuencia, asi que el padre siempre
        // se creo antes y tiene Id menor. Es la condicion que hace imposible un ciclo.
        var flat = new[] { Node(5, parentId: 9, depth: 1), Node(9) };

        var result = CommentTreeBuilder.Build(flat);

        Assert.Equal([5, 9], IdsOf(result));
        Assert.Equal(2, CommentTreeBuilder.CountNodes(result));
    }

    [Fact]
    public void Build_ConUnCicloMutuo_NoProduceUnaEstructuraInfinita()
    {
        // 5 apunta a 6 y 6 apunta a 5. Si el builder aceptara las dos aristas, el
        // arbol seria ciclico y serializarlo a JSON no terminaria nunca.
        var flat = new[]
        {
            Node(5, parentId: 6, depth: 1),
            Node(6, parentId: 5, depth: 1)
        };

        var result = CommentTreeBuilder.Build(flat);

        Assert.Equal(2, CommentTreeBuilder.CountNodes(result));

        var root = Assert.Single(result);
        Assert.Equal(5, root.Id);
        Assert.Equal(6, Assert.Single(root.Replies).Id);
        Assert.Empty(root.Replies[0].Replies);
    }

    [Fact]
    public void Build_ConIdsDuplicados_ConservaSoloLaPrimeraAparicion()
    {
        var flat = new[] { Node(1), Node(1), Node(2, parentId: 1, depth: 1) };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        Assert.Equal(1, root.Id);
        Assert.Equal(2, CommentTreeBuilder.CountNodes(result));
    }

    // ------------------------------------------------------------------
    // Borrado logico
    // ------------------------------------------------------------------

    [Fact]
    public void Build_ConUnPadreBorrado_ConservaSusRespuestas()
    {
        // Es la razon de ser del borrado logico: el nodo sobrevive como "[eliminado]"
        // y el hilo de terceros no se rompe.
        var flat = new[]
        {
            Node(1, isDeleted: true),
            Node(2, parentId: 1, depth: 1),
            Node(3, parentId: 2, depth: 2)
        };

        var result = CommentTreeBuilder.Build(flat);

        var root = Assert.Single(result);
        Assert.True(root.IsDeleted);
        Assert.Null(root.Text);
        Assert.Equal(3, CommentTreeBuilder.CountNodes(result));
        Assert.Equal(3, root.Replies[0].Replies[0].Id);
    }

    // ------------------------------------------------------------------
    // Invariante general
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(5_000)]
    public void Build_CadaComentarioDeLaEntradaApareceExactamenteUnaVez(int total)
    {
        var flat = BuildPseudoRandomThread(total, seed: 1234);

        var result = CommentTreeBuilder.Build(flat);
        var flattenedIds = IdsOf(CommentTreeBuilder.Flatten(result));

        Assert.Equal(total, flattenedIds.Length);
        Assert.Equal(total, flattenedIds.Distinct().Count());
        Assert.Equal(
            flat.Select(c => c.Id).Order(),
            flattenedIds.Order());
    }

    [Fact]
    public void Build_MantieneLaRelacionPadreHijoDeCadaNodo()
    {
        var flat = BuildPseudoRandomThread(500, seed: 99);

        var result = CommentTreeBuilder.Build(flat);
        var byId = flat.ToDictionary(c => c.Id);

        foreach (var node in CommentTreeBuilder.Flatten(result))
        {
            foreach (var reply in node.Replies)
            {
                Assert.Equal(node.Id, reply.ParentCommentId);
                Assert.True(byId.ContainsKey(reply.Id));
            }
        }
    }

    // ------------------------------------------------------------------
    // Flatten
    // ------------------------------------------------------------------

    [Fact]
    public void Flatten_RecorreEnProfundidadRespetandoElOrden()
    {
        //  1                 5
        //  ├── 2             └── 6
        //  │   └── 3
        //  └── 4
        var flat = new[]
        {
            Node(1),
            Node(2, parentId: 1, depth: 1),
            Node(3, parentId: 2, depth: 2),
            Node(4, parentId: 1, depth: 1),
            Node(5),
            Node(6, parentId: 5, depth: 1)
        };

        var result = CommentTreeBuilder.Build(flat);

        Assert.Equal([1, 2, 3, 4, 5, 6], IdsOf(CommentTreeBuilder.Flatten(result)));
    }

    [Fact]
    public void Flatten_ConArbolVacio_NoDevuelveNada()
    {
        Assert.Empty(CommentTreeBuilder.Flatten([]));
        Assert.Equal(0, CommentTreeBuilder.CountNodes([]));
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    /// <summary>
    /// Genera un hilo con forma variada pero determinista: cada comentario elige como
    /// padre alguno anterior (o ninguno), que es exactamente la invariante que la base
    /// garantiza.
    /// </summary>
    private static List<CommentNodeDto> BuildPseudoRandomThread(int total, int seed)
    {
        var random = new Random(seed);
        var comments = new List<CommentNodeDto>(total);
        var depthById = new Dictionary<int, int>(total);

        for (var id = 1; id <= total; id++)
        {
            int? parentId = null;
            var depth = 0;

            // El primero es siempre raiz; despues, 1 de cada 4 tambien lo es.
            if (id > 1 && random.Next(4) != 0)
            {
                var candidate = random.Next(1, id);

                if (depthById[candidate] + 1 < 50)
                {
                    parentId = candidate;
                    depth = depthById[candidate] + 1;
                }
            }

            depthById[id] = depth;
            comments.Add(Node(id, parentId, depth, minutesAfterOrigin: id));
        }

        return comments;
    }
}
