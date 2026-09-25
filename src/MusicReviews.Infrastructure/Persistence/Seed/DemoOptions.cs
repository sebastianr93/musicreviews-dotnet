namespace MusicReviews.Infrastructure.Persistence.Seed;

/// <summary>
/// Contenido de demostracion para la instancia publica.
/// </summary>
/// <remarks>
/// Una instancia recien desplegada arranca con el catalogo vacio, y el home de esta
/// aplicacion es un feed: sin albumes, sin reseñas y sin usuarios no muestra nada. Quien
/// entra por primera vez no ve una aplicacion vacia, ve una aplicacion rota.
/// Esto existe para que eso no pase.
/// </remarks>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>
    /// Apagado por defecto. Se enciende solo en la instancia publica: en desarrollo
    /// ensuciaria la base local y en los tests haria fallar todo lo que cuenta filas.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Contraseña de las cuentas de demostracion. Sin valor no se crea ninguna cuenta:
    /// una contraseña por defecto en el codigo es un usuario conocido con clave conocida
    /// en toda instancia que alguien despliegue.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Cuantos albumes sembrar como maximo. Cada uno cuesta dos peticiones a MusicBrainz
    /// y el limite es de una por segundo, asi que el numero se traduce casi directo a
    /// segundos de siembra.
    /// </summary>
    public int MaxAlbums { get; set; } = 24;
}
