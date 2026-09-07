namespace MusicReviews.Application.Common.Models;

/// <summary>
/// Pagina de un listado que se recorre por cursor en vez de por numero de pagina.
/// </summary>
/// <remarks>
/// Los feeds no se pueden paginar por offset. Entre que el usuario pide la pagina 1 y
/// la 2, alguien publica una reseña: esa fila entra al principio, corre todo un lugar,
/// y la primera fila de la pagina 2 es la que el usuario ya vio al final de la 1.
/// Con contenido que se agrega constantemente, ese salto no es un caso raro sino lo
/// normal. El cursor apunta a una posicion concreta del orden, no a un desplazamiento,
/// asi que lo que se inserte arriba no afecta lo que sigue.
/// </remarks>
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor)
{
    /// <summary>Hay mas resultados si el servidor devolvio por donde seguir.</summary>
    public bool HasMore => NextCursor is not null;

    public static CursorPage<T> Empty() => new([], null);
}

/// <summary>Parametros de una consulta paginada por cursor.</summary>
public sealed record CursorRequest
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public CursorRequest(string? cursor = null, int limit = DefaultLimit)
    {
        Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor;

        Limit = limit switch
        {
            < 1 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => limit
        };
    }

    /// <summary>Null en la primera pagina.</summary>
    public string? Cursor { get; }

    public int Limit { get; }
}
