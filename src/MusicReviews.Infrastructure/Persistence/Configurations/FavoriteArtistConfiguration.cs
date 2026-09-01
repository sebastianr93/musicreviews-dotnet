using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class FavoriteArtistConfiguration : IEntityTypeConfiguration<FavoriteArtist>
{
    public void Configure(EntityTypeBuilder<FavoriteArtist> builder)
    {
        builder.ToTable("FavoriteArtists");

        // PK compuesta: no hace falta Id sustituto y ya impide el duplicado.
        builder.HasKey(f => new { f.UserId, f.ArtistId });

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        // "Quienes tienen a este artista como favorito" / conteo por artista.
        builder.HasIndex(f => f.ArtistId);

        builder.HasOne(f => f.User)
            .WithMany(u => u.FavoriteArtists)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Artist)
            .WithMany(a => a.FavoritedBy)
            .HasForeignKey(f => f.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
