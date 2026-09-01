using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("Albums");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.MusicBrainzId)
            .IsRequired()
            .HasMaxLength(Album.MusicBrainzIdLength)
            .IsFixedLength();

        builder.HasIndex(a => a.MusicBrainzId)
            .IsUnique();

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(Album.TitleMaxLength);

        builder.HasIndex(a => a.Title);

        builder.Property(a => a.CoverArtUrl)
            .HasMaxLength(Album.UrlMaxLength);

        builder.Property(a => a.PrimaryType)
            .HasMaxLength(Album.PrimaryTypeMaxLength);

        builder.Property(a => a.CachedAt)
            .IsRequired();

        // Discografia de un artista.
        builder.HasIndex(a => a.ArtistId);

        builder.HasOne(a => a.Artist)
            .WithMany(ar => ar.Albums)
            .HasForeignKey(a => a.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
