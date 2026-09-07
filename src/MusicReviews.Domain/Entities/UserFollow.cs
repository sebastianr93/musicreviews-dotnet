namespace MusicReviews.Domain.Entities;

/// <summary>
/// Relacion "sigue a" entre dos usuarios. Es dirigida: que A siga a B no implica lo
/// contrario.
/// </summary>
/// <remarks>
/// La clave primaria compuesta (<see cref="FollowerId"/>, <see cref="FollowedId"/>) es
/// la que impide seguir dos veces a la misma persona: no hace falta ni un Id sustituto
/// ni un chequeo previo en la aplicacion, que ademas seria vulnerable a un doble click.
///
/// Un CHECK en la base rechaza que un usuario se siga a si mismo. Podria validarse solo
/// en el servicio, pero una fila asi ensuciaria todos los contadores y el feed sin que
/// ninguna consulta la delate.
/// </remarks>
public class UserFollow
{
    /// <summary>Quien sigue.</summary>
    public Guid FollowerId { get; set; }
    public ApplicationUser Follower { get; set; } = null!;

    /// <summary>A quien sigue.</summary>
    public Guid FollowedId { get; set; }
    public ApplicationUser Followed { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
