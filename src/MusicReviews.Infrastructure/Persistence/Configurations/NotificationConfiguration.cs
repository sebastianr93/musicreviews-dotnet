using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(n => n.IsRead)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(n => n.CreatedAt).IsRequired();

        // Listado: los avisos de un usuario, mas recientes primero. El desempate por Id
        // lo necesita el cursor de paginacion, que filtra por (CreatedAt, Id).
        builder.HasIndex(n => new { n.RecipientId, n.CreatedAt, n.Id })
            .IsDescending(false, true, true);

        // Indice PARCIAL para el contador de no leidas. Es la consulta mas frecuente de
        // toda la aplicacion —se pide en cada carga de pagina para pintar el globito— y
        // lo normal es tener pocas sin leer sobre un historial largo. Un indice completo
        // crece con todo el historial; este solo indexa las filas que la consulta mira.
        builder.HasIndex(n => n.RecipientId)
            .HasFilter("NOT \"IsRead\"")
            .HasDatabaseName("IX_Notifications_RecipientId_Unread");

        // Para encontrar un aviso equivalente ya existente y no duplicarlo cuando el
        // mismo usuario retira y vuelve a poner un voto.
        builder.HasIndex(n => new { n.RecipientId, n.ActorId, n.Type, n.ReviewId, n.CommentId });

        builder.HasOne(n => n.Recipient)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Si se borra quien la provoco, el aviso deja de tener sentido.
        builder.HasOne(n => n.Actor)
            .WithMany()
            .HasForeignKey(n => n.ActorId)
            .OnDelete(DeleteBehavior.Cascade);

        // Borrar el contenido borra los avisos que apuntaban a el: un aviso que lleva
        // a una resenia que ya no existe es un 404 con forma de notificacion.
        builder.HasOne(n => n.Review)
            .WithMany()
            .HasForeignKey(n => n.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.Comment)
            .WithMany()
            .HasForeignKey(n => n.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
