namespace MusicReviews.Domain.Entities;

/// <summary>
/// Imagen de perfil subida por un usuario, con los bytes adentro de la base.
/// </summary>
/// <remarks>
/// <para>
/// Guardar binarios en la base no es lo que uno elige por gusto: lo natural es un bucket
/// de objetos. Aca se hace porque el destino de despliegue tiene <b>filesystem efimero</b>
/// —el contenedor se recrea en cada version y en cada arranque en frio— y un archivo
/// escrito en disco desaparece sin aviso. Entre perder las fotos y sumar un servicio mas
/// para administrar, la base gana: ya esta, ya tiene respaldo y ya es transaccional.
/// </para>
/// <para>
/// El tamanio esta acotado por el tope de subida (2 MB), asi que el peor caso es
/// predecible. Si algun dia hace falta un bucket, <c>IAvatarStorage</c> es la unica pieza
/// que cambia.
/// </para>
/// <para>
/// Es tabla aparte y no una columna en <c>ApplicationUser</c> a proposito: una columna
/// <c>bytea</c> ahi viajaria en cada consulta que traiga un usuario —y son casi todas—
/// salvo que cada proyeccion se acuerde de excluirla. Una tabla separada hace que el
/// costo se pague solo cuando alguien pide la imagen.
/// </para>
/// </remarks>
public class UserAvatar
{
    public const int ContentTypeMaxLength = 100;

    /// <summary>
    /// Identificador propio de la imagen, no del usuario: es lo que aparece en la URL.
    /// </summary>
    /// <remarks>
    /// Cambiar la foto crea una fila nueva con otro Id, asi que la URL anterior deja de
    /// resolver por si sola. Eso es lo que permite servirlas con cache agresiva sin
    /// depender de que ningun intermediario haga caso a una invalidacion.
    /// </remarks>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>Bytes de la imagen, tal como se subieron.</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>Content-Type derivado de los bytes magicos, nunca de lo que declaro el cliente.</summary>
    public string ContentType { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
