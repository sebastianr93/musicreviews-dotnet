using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.ToTable("Artists");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.MusicBrainzId)
            .IsRequired()
            .HasMaxLength(Artist.MusicBrainzIdLength)
            .IsFixedLength();

        // Clave natural: impide que dos sincronizaciones concurrentes dupliquen el mismo artista.
        builder.HasIndex(a => a.MusicBrainzId)
            .IsUnique();

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(Artist.NameMaxLength);

        // Busqueda local por nombre antes de salir a MusicBrainz.
        builder.HasIndex(a => a.Name);

        builder.Property(a => a.Disambiguation)
            .HasMaxLength(Artist.DisambiguationMaxLength);

        builder.Property(a => a.Country)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.Property(a => a.Type)
            .HasMaxLength(50);

        builder.Property(a => a.ImageUrl)
            .HasMaxLength(Artist.UrlMaxLength);

        builder.Property(a => a.CachedAt)
            .IsRequired();
    }
}
