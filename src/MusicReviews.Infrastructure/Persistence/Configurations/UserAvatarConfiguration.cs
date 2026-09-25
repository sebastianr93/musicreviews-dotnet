using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class UserAvatarConfiguration : IEntityTypeConfiguration<UserAvatar>
{
    public void Configure(EntityTypeBuilder<UserAvatar> builder)
    {
        builder.ToTable("UserAvatars");

        builder.HasKey(a => a.Id);

        // La clave la genera la aplicacion, no la base: el Id forma parte de la URL y
        // hace falta conocerlo antes de guardar para poder devolverla en la misma
        // operacion.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Content)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(UserAvatar.ContentTypeMaxLength);

        builder.Property(a => a.CreatedAt).IsRequired();

        // No es unico a proposito: reemplazar una foto inserta la nueva y borra la vieja
        // despues, asi que hay un instante con dos filas del mismo usuario. Un indice
        // unico haria fallar justo el camino normal.
        builder.HasIndex(a => a.UserId);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
