using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> builder)
    {
        builder.ToTable("UserFollows", t => t.HasCheckConstraint(
            "CK_UserFollows_NoSelfFollow",
            "\"FollowerId\" <> \"FollowedId\""));

        // PK compuesta: sin Id sustituto, y ya impide el seguimiento duplicado.
        builder.HasKey(f => new { f.FollowerId, f.FollowedId });

        builder.Property(f => f.CreatedAt).IsRequired();

        // "A quienes sigo, mas recientes primero".
        builder.HasIndex(f => new { f.FollowerId, f.CreatedAt })
            .IsDescending(false, true);

        // "Quienes me siguen". La PK no sirve para esto: su columna lider es FollowerId.
        builder.HasIndex(f => new { f.FollowedId, f.CreatedAt })
            .IsDescending(false, true);

        // Dos FK a la misma tabla, las dos en cascada: al borrar una cuenta desaparecen
        // sus relaciones en ambos sentidos. PostgreSQL admite multiples caminos de
        // cascada sin problema (a diferencia de SQL Server, donde esto no compilaria).
        builder.HasOne(f => f.Follower)
            .WithMany(u => u.Following)
            .HasForeignKey(f => f.FollowerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Followed)
            .WithMany(u => u.Followers)
            .HasForeignKey(f => f.FollowedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
