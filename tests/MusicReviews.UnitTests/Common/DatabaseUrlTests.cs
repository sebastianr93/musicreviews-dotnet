using MusicReviews.Application.Common;

namespace MusicReviews.UnitTests.Common;

/// <summary>
/// La traduccion de DATABASE_URL a una cadena de Npgsql.
/// </summary>
/// <remarks>
/// Se verifica aca y no en el despliegue por una razon practica: el sintoma de un parseo
/// mal hecho es que la aplicacion no arranca en produccion, con un error de autenticacion
/// o de "keyword not supported" que no dice cual de las cinco partes de la URL salio mal.
/// </remarks>
public class DatabaseUrlTests
{
    private static string Value(string connectionString, string key) =>
        connectionString
            .Split(';')
            .Single(part => part.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];

    [Theory]
    [InlineData("postgres://u:p@host/db")]
    [InlineData("postgresql://u:p@host/db")]
    [InlineData("POSTGRES://u:p@host/db")]
    public void IsUri_ReconoceLosEsquemasDePostgres(string url) =>
        Assert.True(DatabaseUrl.IsUri(url));

    [Theory]
    [InlineData("Host=localhost;Database=db")]
    [InlineData("mysql://u:p@host/db")]
    [InlineData("")]
    [InlineData(null)]
    public void IsUri_RechazaLoQueNoLoEs(string? value) =>
        Assert.False(DatabaseUrl.IsUri(value));

    [Fact]
    public void ToConnectionString_DesarmaLaUrlCompleta()
    {
        var result = DatabaseUrl.ToConnectionString("postgres://alice:secreto@db.example.com:6543/musicreviews");

        Assert.Equal("db.example.com", Value(result, "Host"));
        Assert.Equal("6543", Value(result, "Port"));
        Assert.Equal("musicreviews", Value(result, "Database"));
        Assert.Equal("alice", Value(result, "Username"));
        Assert.Equal("secreto", Value(result, "Password"));
    }

    [Fact]
    public void ToConnectionString_SinPuerto_UsaElDePostgres()
    {
        // Neon publica la URL sin puerto.
        var result = DatabaseUrl.ToConnectionString("postgres://alice:secreto@ep-cool.neon.tech/musicreviews");

        Assert.Equal("5432", Value(result, "Port"));
    }

    [Fact]
    public void ToConnectionString_DecodificaLaContraseña()
    {
        // Este es el caso que rompe el despliegue: las contraseñas generadas al azar
        // llevan @ y / con frecuencia, y en la URI viajan percent-encoded. Sin decodificar,
        // el servidor rechaza la autenticacion y el error apunta a las credenciales, no
        // al parseo.
        var result = DatabaseUrl.ToConnectionString("postgres://alice:p%40ss%2Fword@host/db");

        Assert.Equal("p@ss/word", Value(result, "Password"));
    }

    [Fact]
    public void ToConnectionString_EntrecomillaUnaContraseñaConPuntoYComa()
    {
        // El ; es el separador de la cadena de Npgsql: sin comillas la parte
        // de mas queda interpretada como otra clave.
        var result = DatabaseUrl.ToConnectionString("postgres://alice:a%3Bb@host/db");

        Assert.Contains("Password=\"a;b\"", result);
    }

    [Fact]
    public void ToConnectionString_ExigeTlsPorDefecto()
    {
        var result = DatabaseUrl.ToConnectionString("postgres://alice:secreto@host/db");

        Assert.Equal("Require", Value(result, "SSL Mode"));
    }

    [Fact]
    public void ToConnectionString_RespetaElSslmodeDeLaUrl()
    {
        var result = DatabaseUrl.ToConnectionString("postgres://alice:secreto@host/db?sslmode=Disable");

        Assert.Equal("Disable", Value(result, "SSL Mode"));
    }

    [Fact]
    public void ToConnectionString_LimitaElPool()
    {
        // Contra una base de plan gratuito el limite de conexiones lo pone el servidor;
        // un pool mas grande solo adelanta el momento en que se agota.
        var result = DatabaseUrl.ToConnectionString("postgres://alice:secreto@host/db");

        Assert.Equal("20", Value(result, "Maximum Pool Size"));
    }

    [Theory]
    [InlineData("postgres://host")]
    [InlineData("postgres://u:p@host/")]
    [InlineData("no-es-una-url")]
    public void ToConnectionString_ConUnaUrlIncompleta_Falla(string url) =>
        Assert.Throws<FormatException>(() => DatabaseUrl.ToConnectionString(url));

    [Fact]
    public void Normalize_DejaPasarUnaCadenaDeNpgsqlSinTocarla()
    {
        // La misma variable de configuracion acepta los dos formatos: en desarrollo se
        // escribe la cadena de Npgsql a mano, en produccion la pone la plataforma.
        const string existing = "Host=localhost;Database=musicreviews;Username=dev";

        Assert.Equal(existing, DatabaseUrl.Normalize(existing));
    }

    [Fact]
    public void Normalize_ConVacio_DevuelveVacio() =>
        Assert.Equal(string.Empty, DatabaseUrl.Normalize(string.Empty));
}
