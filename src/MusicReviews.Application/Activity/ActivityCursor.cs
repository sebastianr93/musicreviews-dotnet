using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MusicReviews.Application.Activity.Dtos;

namespace MusicReviews.Application.Activity;

/// <summary>
/// Posicion dentro del timeline: el instante y el identificador de la ultima entrada
/// entregada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que el cursor no es solo la fecha.</b> El timeline se arma mezclando reseñas,
/// comentarios y votos de tablas distintas, y nada impide que dos entradas compartan
/// el instante exacto —basta con votar y comentar en la misma operacion, o con dos
/// escrituras dentro de la misma transaccion—. Con un cursor que solo lleve la fecha,
/// un filtro <c>&lt;</c> se saltea las entradas empatadas y un <c>&lt;=</c> las repite.
/// Agregando el Id, el orden queda <b>total</b> y no hay ambiguedad sobre donde seguir.
/// </para>
/// <para>
/// La consulta a la base filtra por <c>CreatedAt &lt;= cursor.CreatedAt</c> —incluyente,
/// para no perder los empates— y el descarte fino lo hace <see cref="IsAfter"/> en
/// memoria, donde el Id ya esta disponible.
/// </para>
/// <para>
/// Se serializa en Base64Url para que el cliente lo trate como un valor opaco. No es
/// seguridad: es que un cursor que parece una fecha invita a construirlo a mano, y ahi
/// el contrato se rompe en cuanto cambie el criterio de orden.
/// </para>
/// </remarks>
public sealed record ActivityCursor(DateTimeOffset CreatedAt, string Id)
{
    private const char Separator = '|';

    /// <summary>Orden del timeline: mas reciente primero, y el Id desempata.</summary>
    public static int Compare(ActivityEntryDto left, ActivityEntryDto right)
    {
        var byDate = right.CreatedAt.CompareTo(left.CreatedAt);

        return byDate != 0
            ? byDate
            : string.CompareOrdinal(right.Id, left.Id);
    }

    /// <summary>
    /// true si la entrada va despues de esta posicion, es decir, si todavia no fue
    /// entregada.
    /// </summary>
    public bool IsAfter(ActivityEntryDto entry)
    {
        if (entry.CreatedAt != CreatedAt)
        {
            return entry.CreatedAt < CreatedAt;
        }

        // Mismo instante: decide el Id, con el mismo criterio que el orden.
        return string.CompareOrdinal(entry.Id, Id) < 0;
    }

    public static ActivityCursor From(ActivityEntryDto entry) => new(entry.CreatedAt, entry.Id);

    public string Encode()
    {
        // Ticks UTC y no milisegundos: PostgreSQL guarda timestamptz con precision de
        // microsegundos, y truncar a milisegundos haria que el instante decodificado
        // nunca coincida exactamente con el de la fila. IsAfter compara por igualdad
        // para detectar los empates, asi que perder precision lo rompe en silencio.
        var raw = $"{CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}{Separator}{Id}";

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// Decodifica un cursor recibido del cliente. Devuelve null ante cualquier valor
    /// invalido: un cursor corrupto tiene que degradar a "empezar de cero", no romper
    /// la peticion con un 500.
    /// </summary>
    public static ActivityCursor? TryDecode(string? value)
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
                || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return null;
            }

            return new ActivityCursor(
                new DateTimeOffset(ticks, TimeSpan.Zero),
                raw[(separator + 1)..]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
