using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews", t => t.HasCheckConstraint(
            "CK_Reviews_Score_Range",
            $"\"Score\" >= {Review.MinScore} AND \"Score\" <= {Review.MaxScore}"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Score)
            .IsRequired();

        builder.Property(r => r.Text)
            .IsRequired()
            .HasMaxLength(Review.TextMaxLength);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // Regla de negocio sostenida por la base: una sola review por usuario y album.
        // Ademas sirve como indice de "las reviews de un usuario" (UserId es la columna lider).
        builder.HasIndex(r => new { r.UserId, r.AlbumId })
            .IsUnique();

        // Listado de reviews de un album, ordenado por fecha.
        builder.HasIndex(r => new { r.AlbumId, r.CreatedAt })
            .IsDescending(false, true);

        // Timeline de actividad. El unico (UserId, AlbumId) no sirve para esto:
        // ordena por album, no por fecha.
        builder.HasIndex(r => new { r.UserId, r.CreatedAt })
            .IsDescending(false, true);

        builder.HasOne(r => r.User)
            .WithMany(u => u.Reviews)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Album)
            .WithMany(a => a.Reviews)
            .HasForeignKey(r => r.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
