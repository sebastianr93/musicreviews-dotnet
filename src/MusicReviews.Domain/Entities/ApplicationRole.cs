using Microsoft.AspNetCore.Identity;

namespace MusicReviews.Domain.Entities;

/// <summary>
/// Rol de la aplicacion. Se declara explicitamente (en vez de usar IdentityRole&lt;Guid&gt;
/// directamente) para poder extenderlo mas adelante sin una migracion de tipo.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
