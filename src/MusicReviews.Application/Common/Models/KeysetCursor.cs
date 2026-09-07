using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace MusicReviews.Application.Common.Models;

/// <summary>
/// Posicion dentro de un listado ordenado por (fecha descendente, Id descendente).
/// </summary>
/// <remarks>
/// <para>
/// A diferencia del cursor del timeline —que mezcla varias tablas y tiene que desempatar
/// con un Id compuesto de texto— este apunta a una sola tabla con Id numerico, asi que
/// la condicion de keyset se traduce entera a SQL:
/// <c>CreatedAt &lt; @t OR (CreatedAt = @t AND Id &lt; @id)</c>. No hace falta traer de
/// mas ni descartar en memoria: la base devuelve exactamente lo que corresponde.
/// </para>
/// <para>
/// La fecha se codifica en ticks UTC porque PostgreSQL guarda <c>timestamptz</c> con
/// precision de microsegundos: truncar a milisegundos haria que la comparacion de
/// igualdad nunca acierte y el desempate por Id dejara de aplicarse.
/// </para>
/// </remarks>
public sealed record KeysetCursor(DateTimeOffset CreatedAt, long Id)
{
    private const char Separator = '|';

    public string Encode()
    {
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{CreatedAt.UtcTicks}{Separator}{Id}");

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// Devuelve null ante cualquier valor invalido: un cursor corrupto tiene que
    /// degradar a "empezar de cero", no romper la peticion.
    /// </summary>
    public static KeysetCursor? TryDecode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
            var separator = raw.IndexOf(Separator);

            if (separator <= 0 || separator == raw.Length - 1)
            {
                return null;
            }

            if (!long.TryParse(
                    raw.AsSpan(0, separator),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var ticks)
                || ticks < 0
                || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !long.TryParse(
                    raw.AsSpan(separator + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var id))
            {
                return null;
            }

            return new KeysetCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
