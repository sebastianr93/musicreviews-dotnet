using MusicReviews.Application.Activity;
using MusicReviews.Application.Activity.Dtos;
using MusicReviews.Application.Reviews.Dtos;

namespace MusicReviews.UnitTests.Activity;

/// <summary>
/// Tests del cursor del timeline.
/// </summary>
/// <remarks>
/// La paginacion por cursor tiene una invariante que no se ve a simple vista: recorrer
/// el feed pagina por pagina tiene que visitar cada entrada <b>exactamente una vez</b>.
/// Si el cursor pierde precision o el orden no es total, el sintoma es una entrada
/// repetida o una que nunca aparece, y nadie lo nota hasta que el feed tiene volumen.
/// </remarks>
public class ActivityCursorTests
{
    private static readonly DateTimeOffset Origin = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly AuthorDto Author =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "seba", null);

    private static ActivityEntryDto Entry(string id, DateTimeOffset createdAt) =>
        new(id, ActivityKind.ReviewPublished, createdAt, Author, null, null, null, null);

    // ------------------------------------------------------------------
    // Serializacion
    // ------------------------------------------------------------------

    [Fact]
    public void Encode_Decode_PreservaElInstanteYElId()
    {
        var cursor = new ActivityCursor(Origin, "review:42");

        var decoded = ActivityCursor.TryDecode(cursor.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(cursor.CreatedAt, decoded!.CreatedAt);
        Assert.Equal(cursor.Id, decoded!.Id);
    }

    [Fact]
    public void Encode_Decode_ConservaLaPrecisionDeMicrosegundos()
    {
        // PostgreSQL guarda timestamptz con precision de microsegundos. Si el cursor
        // truncara a milisegundos, el instante decodificado nunca coincidiria con el
        // de la fila y la deteccion de empates dejaria de funcionar.
        var precise = Origin.AddTicks(1234567);
        var cursor = new ActivityCursor(precise, "like:7");

        var decoded = ActivityCursor.TryDecode(cursor.Encode());

        Assert.Equal(precise, decoded!.CreatedAt);
    }

    [Fact]
    public void Encode_NoProduceCaracteresQueHayaQueEscaparEnUnaQueryString()
    {
        var encoded = new ActivityCursor(Origin, "comment:99").Encode();

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("esto-no-es-base64-!!!")]
    [InlineData("c2luLXNlcGFyYWRvcg")]
    [InlineData("fGVtcHR5LXRpbWVzdGFtcA")]
    public void TryDecode_ConValorInvalido_DevuelveNullEnVezDeFallar(string? value)
    {
        // Un cursor corrupto o inventado tiene que degradar a "empezar desde el
        // principio", no tumbar la peticion con un 500.
        Assert.Null(ActivityCursor.TryDecode(value));
    }

    // ------------------------------------------------------------------
    // Posicion
    // ------------------------------------------------------------------

    [Fact]
    public void IsAfter_ConUnaEntradaMasNueva_DevuelveFalse()
    {
        var cursor = new ActivityCursor(Origin, "review:10");

        Assert.False(cursor.IsAfter(Entry("review:11", Origin.AddMinutes(1))));
    }

    [Fact]
    public void IsAfter_ConUnaEntradaMasVieja_DevuelveTrue()
    {
        var cursor = new ActivityCursor(Origin, "review:10");

        Assert.True(cursor.IsAfter(Entry("review:9", Origin.AddMinutes(-1))));
    }

    [Fact]
    public void IsAfter_ConElMismoInstante_DesempataPorId()
    {
        // Es el caso que motiva incluir el Id en el cursor: votar y comentar en la
        // misma operacion produce dos entradas con el instante exacto compartido.
        var cursor = new ActivityCursor(Origin, "review:50");

        Assert.True(cursor.IsAfter(Entry("review:49", Origin)));
        Assert.False(cursor.IsAfter(Entry("review:51", Origin)));
    }

    [Fact]
    public void IsAfter_ConLaEntradaDelPropioCursor_DevuelveFalse()
    {
        var entry = Entry("review:50", Origin);
        var cursor = ActivityCursor.From(entry);

        // Si devolviera true, la ultima entrada de cada pagina se repetiria al principio
        // de la siguiente.
        Assert.False(cursor.IsAfter(entry));
    }

    // ------------------------------------------------------------------
    // Orden
    // ------------------------------------------------------------------

    [Fact]
    public void Compare_OrdenaDeMasRecienteAMasAntiguo()
    {
        var entries = new[]
        {
            Entry("a:1", Origin.AddMinutes(-5)),
            Entry("a:2", Origin),
            Entry("a:3", Origin.AddMinutes(-10))
        };

        var ordered = entries
            .Order(Comparer<ActivityEntryDto>.Create(ActivityCursor.Compare))
            .Select(e => e.Id)
            .ToArray();

        Assert.Equal(new[] { "a:2", "a:1", "a:3" }, ordered);
    }

    [Fact]
    public void Compare_ConElMismoInstante_ProduceUnOrdenTotal()
    {
        var entries = new[]
        {
            Entry("like:2", Origin),
            Entry("like:1", Origin),
            Entry("like:3", Origin)
        };

        var ordered = entries
            .Order(Comparer<ActivityEntryDto>.Create(ActivityCursor.Compare))
            .Select(e => e.Id)
            .ToArray();

        Assert.Equal(new[] { "like:3", "like:2", "like:1" }, ordered);
    }

    // ------------------------------------------------------------------
    // La invariante que importa
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 3)]
    [InlineData(50, 7)]
    [InlineData(200, 20)]
    public void PaginarElFeedCompleto_VisitaCadaEntradaExactamenteUnaVez(int total, int pageSize)
    {
        // Un tercio de las entradas comparte instante con otra: es justo la situacion
        // que un cursor basado solo en la fecha maneja mal.
        var all = Enumerable.Range(1, total)
            .Select(i => Entry($"entry:{i:d5}", Origin.AddSeconds(-(i / 3))))
            .ToList();

        var comparer = Comparer<ActivityEntryDto>.Create(ActivityCursor.Compare);
        var visited = new List<string>();
        ActivityCursor? cursor = null;

        for (var guard = 0; guard < total + 10; guard++)
        {
            var page = all
                .Where(entry => cursor is null || cursor.IsAfter(entry))
                .Order(comparer)
                .Take(pageSize)
                .ToList();

            if (page.Count == 0)
            {
                break;
            }

            visited.AddRange(page.Select(e => e.Id));
            cursor = ActivityCursor.From(page[^1]);
        }

        Assert.Equal(total, visited.Count);
        Assert.Equal(total, visited.Distinct().Count());
        Assert.Equal(all.Select(e => e.Id).Order(), visited.Order());
    }

    [Fact]
    public void PaginarConTodasLasEntradasEnElMismoInstante_TampocoRepiteNiPierde()
    {
        // Caso extremo: una importacion masiva escrita en la misma transaccion.
        var all = Enumerable.Range(1, 25)
            .Select(i => Entry($"bulk:{i:d3}", Origin))
            .ToList();

        var comparer = Comparer<ActivityEntryDto>.Create(ActivityCursor.Compare);
        var visited = new List<string>();
        ActivityCursor? cursor = null;

        while (true)
        {
            var page = all
                .Where(entry => cursor is null || cursor.IsAfter(entry))
                .Order(comparer)
                .Take(4)
                .ToList();

            if (page.Count == 0)
            {
                break;
            }

            visited.AddRange(page.Select(e => e.Id));
            cursor = ActivityCursor.From(page[^1]);
        }

        Assert.Equal(25, visited.Count);
        Assert.Equal(25, visited.Distinct().Count());
    }
}
