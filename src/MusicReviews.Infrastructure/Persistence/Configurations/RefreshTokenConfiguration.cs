using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(RefreshToken.TokenHashLength)
            .IsFixedLength();

        // Cada refresh entra por el hash: sin este indice unico habria un seq scan
        // por request y nada impediria colisiones logicas.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.Property(t => t.CreatedByIp)
            .HasMaxLength(RefreshToken.IpMaxLength);

        builder.Property(t => t.RevokedByIp)
            .HasMaxLength(RefreshToken.IpMaxLength);

        builder.Property(t => t.ReplacedByTokenHash)
            .HasMaxLength(RefreshToken.TokenHashLength)
            .IsFixedLength();

        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        // Revocar en bloque los tokens activos de un usuario (deteccion de reuso) y
        // limpiar los vencidos.
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Las propiedades calculadas del dominio no se persisten.
        builder.Ignore(t => t.IsRevoked);
    }
}
