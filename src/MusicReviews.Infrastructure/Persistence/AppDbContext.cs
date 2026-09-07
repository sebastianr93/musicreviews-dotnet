using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence;

/// <summary>
/// DbContext unico de la aplicacion. Hereda de <see cref="IdentityDbContext{TUser,TRole,TKey}"/>
/// para que las tablas de Identity (AspNetUsers, AspNetRoles, AspNetUserRoles, ...) y las tablas
/// del dominio vivan en la misma base y compartan transaccion.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Like> Likes => Set<Like>();
    public DbSet<FavoriteArtist> FavoriteArtists => Set<FavoriteArtist>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserFollow> UserFollows => Set<UserFollow>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Primero el modelo de Identity, despues las configuraciones propias.
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
