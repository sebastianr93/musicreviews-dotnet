using MusicReviews.Application.Common.Models;

namespace MusicReviews.UnitTests.Common;

/// <summary>
/// El cursor de listados de una sola tabla. Lo que se prueba aca es que el viaje de ida
/// y vuelta no pierda precision y que ninguna entrada invalida haga fallar la peticion.
/// </summary>
public class KeysetCursorTests
{
    [Fact]
    public void Encode_Decode_DevuelveElMismoValor()
    {
        var original = new KeysetCursor(
            new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.Zero), 42);

        var decoded = KeysetCursor.TryDecode(original.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(original.CreatedAt.UtcTicks, decoded!.CreatedAt.UtcTicks);
        Assert.Equal(42, decoded.Id);
    }

    [Fact]
    public void Encode_ConservaLosMicrosegundos()
    {
        // Postgres guarda timestamptz con precision de microsegundos. Si el cursor
        // truncara a milisegundos, la comparacion de igualdad nunca acertaria y el
        // desempate por Id dejaria de aplicarse: el listado repetiria o saltearia filas
        // justo cuando dos registros comparten el mismo instante.
        var withMicroseconds = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddTicks(1234567);

        var decoded = KeysetCursor.TryDecode(new KeysetCursor(withMicroseconds, 7).Encode());

        Assert.Equal(withMicroseconds.UtcTicks, decoded!.CreatedAt.UtcTicks);
    }

    [Fact]
    public void Encode_NormalizaElHusoHorario()
    {
        var utc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var sameInstantElsewhere = utc.ToOffset(TimeSpan.FromHours(-3));

        Assert.Equal(
            new KeysetCursor(utc, 1).Encode(),
            new KeysetCursor(sameInstantElsewhere, 1).Encode());
    }

    [Fact]
    public void Encode_NoUsaCaracteresQueHayaQueEscaparEnUnaUrl()
    {
        // Viaja como query string. Base64 clasico traeria '+' y '/', que ahi significan
        // otra cosa.
        var encoded = new KeysetCursor(DateTimeOffset.UtcNow, long.MaxValue).Encode();

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-base64-!!")]
    [InlineData("MTIz")]           // "123": base64 valido, sin separador
    [InlineData("MTIzfA")]         // "123|": sin la segunda parte
    [InlineData("fDEyMw")]         // "|123": sin la primera
    [InlineData("YXxi")]           // "a|b": ninguna de las dos es numerica
    [InlineData("LTF8Mg")]         // "-1|2": ticks negativos
    public void TryDecode_ConEntradaInvalida_DevuelveNull(string? value)
    {
        // Un cursor corrupto —recortado al copiar, viejo, inventado— tiene que degradar
        // a "empezar de cero", no romper la peticion con un 500.
        Assert.Null(KeysetCursor.TryDecode(value));
    }

    [Fact]
    public void TryDecode_ConTicksFueraDeRango_DevuelveNull()
    {
        var tooBig = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{long.MaxValue}|1"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Null(KeysetCursor.TryDecode(tooBig));
    }

    [Fact]
    public void TryDecode_AceptaIdsGrandes()
    {
        var decoded = KeysetCursor.TryDecode(
            new KeysetCursor(DateTimeOffset.UnixEpoch, long.MaxValue).Encode());

        Assert.Equal(long.MaxValue, decoded!.Id);
    }
}
