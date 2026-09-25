namespace MusicReviews.Application.Common;

/// <summary>
/// Traduce una URL de conexion en formato URI —<c>postgres://usuario:clave@host:puerto/base</c>—
/// a la cadena de palabras clave que entiende Npgsql.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque las plataformas de hosting no se pusieron de acuerdo con .NET. Neon,
/// Railway, Render, Supabase y Heroku publican la base en una unica variable
/// <c>DATABASE_URL</c> con forma de URI, que es lo que esperan los drivers de Node, Python
/// y Ruby. Npgsql no acepta ese formato: quiere <c>Host=...;Port=...;Database=...</c>.
/// Sin esta traduccion, desplegar significa desarmar la URL a mano y cargar cinco
/// variables de entorno separadas, que es exactamente el paso que despues nadie recuerda
/// haber hecho cuando hay que rotar la contraseña.
/// </para>
/// <para>
/// El usuario y la contraseña vienen percent-encoded en la URI: las contraseñas que
/// generan estas plataformas son aleatorias y llevan <c>@</c>, <c>/</c> y <c>+</c> con
/// frecuencia. Decodificarlas no es opcional —una contraseña con <c>%40</c> sin decodificar
/// falla la autenticacion— y volver a escaparlas para la cadena de Npgsql tampoco, porque
/// ahi el separador es <c>;</c>.
/// </para>
/// <para>
/// Vive en Application y no en Infrastructure para que sea una funcion pura, sin
/// dependencia de Npgsql, y por lo tanto verificable en los tests unitarios: el momento
/// de descubrir que el parseo esta mal no puede ser el primer despliegue.
/// </para>
/// </remarks>
public static class DatabaseUrl
{
    private static readonly string[] Schemes = ["postgres", "postgresql"];

    /// <summary>
    /// Devuelve <see langword="true"/> si el texto tiene forma de URI de PostgreSQL.
    /// </summary>
    /// <remarks>
    /// Permite aceptar indistintamente los dos formatos en la misma variable de
    /// configuracion: si ya es una cadena de Npgsql, se usa tal cual.
    /// </remarks>
    public static bool IsUri(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Convierte la URI en una cadena de conexion de Npgsql.
    /// </summary>
    /// <exception cref="FormatException">El texto no es una URI de PostgreSQL valida.</exception>
    public static string ToConnectionString(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            throw new FormatException(
                "La URL de la base de datos debe tener la forma " +
                "postgres://usuario:clave@host:puerto/base.");
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            throw new FormatException("La URL de la base de datos no indica un host.");
        }

        var database = uri.AbsolutePath.Trim('/');

        if (string.IsNullOrEmpty(database))
        {
            throw new FormatException("La URL de la base de datos no indica una base.");
        }

        var (user, password) = SplitUserInfo(uri.UserInfo);

        // Puerto -1 significa "no venia en la URI". El de PostgreSQL es 5432.
        var port = uri.Port > 0 ? uri.Port : 5432;

        var parts = new List<string>
        {
            Pair("Host", uri.Host),
            Pair("Port", port.ToString()),
            Pair("Database", database)
        };

        if (user.Length > 0)
        {
            parts.Add(Pair("Username", user));
        }

        if (password.Length > 0)
        {
            parts.Add(Pair("Password", password));
        }

        // El TLS no se negocia: estas bases son accesibles desde internet y la conexion
        // sale de otro proveedor. Si la URL trae su propio sslmode, manda ese.
        var query = ParseQuery(uri.Query);

        parts.Add(Pair("SSL Mode", query.GetValueOrDefault("sslmode", "Require")));

        // Los certificados de Neon, Railway y Render los firman autoridades que el
        // contenedor base de .NET no siempre trae en su almacen. Sin esto el arranque
        // falla con un error de cadena de certificados que no dice nada util.
        parts.Add(Pair("Trust Server Certificate", "true"));

        // Un pool grande contra una base de plan gratuito es contraproducente: el limite
        // de conexiones del servidor es bajo y se agota antes de que el pool crezca.
        parts.Add(Pair("Maximum Pool Size", query.GetValueOrDefault("max_pool_size", "20")));

        return string.Join(";", parts);
    }

    /// <summary>
    /// Devuelve la cadena de Npgsql tanto si <paramref name="value"/> ya lo era como si
    /// vino en formato URI. Un valor vacio se propaga como vacio.
    /// </summary>
    public static string? Normalize(string? value) =>
        IsUri(value) ? ToConnectionString(value!) : value;

    private static (string User, string Password) SplitUserInfo(string userInfo)
    {
        if (userInfo.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var separator = userInfo.IndexOf(':');

        return separator < 0
            ? (Uri.UnescapeDataString(userInfo), string.Empty)
            : (Uri.UnescapeDataString(userInfo[..separator]),
               Uri.UnescapeDataString(userInfo[(separator + 1)..]));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');

            if (separator > 0)
            {
                result[Uri.UnescapeDataString(pair[..separator])] =
                    Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return result;
    }

    /// <summary>
    /// Arma un par clave-valor escapando el valor segun las reglas de ADO.NET.
    /// </summary>
    /// <remarks>
    /// Una contraseña generada al azar puede contener <c>;</c> o <c>=</c>, que son los dos
    /// separadores de la cadena. Sin comillas, la cadena queda partida y el error que se
    /// ve es "keyword not supported", que manda a buscar el problema en el lugar
    /// equivocado.
    /// </remarks>
    private static string Pair(string key, string value)
    {
        var needsQuotes = value.Length > 0
            && (value.IndexOfAny([';', '=', '"', '\'']) >= 0
                || char.IsWhiteSpace(value[0])
                || char.IsWhiteSpace(value[^1]));

        return needsQuotes
            ? key + "=\"" + value.Replace("\"", "\"\"") + "\""
            : key + "=" + value;
    }
}
