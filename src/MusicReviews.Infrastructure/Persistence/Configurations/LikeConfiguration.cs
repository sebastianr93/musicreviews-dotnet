using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class LikeConfiguration : IEntityTypeConfiguration<Like>
{
    public void Configure(EntityTypeBuilder<Like> builder)
    {
        builder.ToTable("Likes");

        builder.HasKey(l => l.Id);

        // El enum se persiste como int con valores explicitos (ver LikeTargetType).
        builder.Property(l => l.TargetType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(l => l.TargetId)
            .IsRequired();

        builder.Property(l => l.IsLike)
            .IsRequired();

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        // Un voto por usuario y target: la base rechaza el doble voto aunque
        // dos requests lleguen en paralelo.
        builder.HasIndex(l => new { l.UserId, l.TargetType, l.TargetId })
            .IsUnique();

        // Conteo de likes/dislikes de un target sin tocar la tabla de usuarios.
        builder.HasIndex(l => new { l.TargetType, l.TargetId, l.IsLike });

        // Timeline de actividad: los votos de un usuario, mas recientes primero.
        // El unico (UserId, TargetType, TargetId) no ordena por fecha.
        builder.HasIndex(l => new { l.UserId, l.CreatedAt })
            .IsDescending(false, true);

        builder.HasOne(l => l.User)
            .WithMany(u => u.Likes)
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
