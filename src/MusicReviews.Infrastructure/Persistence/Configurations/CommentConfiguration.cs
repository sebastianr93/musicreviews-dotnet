using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text)
            .IsRequired()
            .HasMaxLength(Comment.TextMaxLength);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.Depth)
            .IsRequired()
            .HasDefaultValue(0);

        // ---------------------------------------------------------------------
        // Indices que sostienen el armado del arbol en una sola query.
        // El hilo completo se trae con WHERE "ReviewId" = @id ORDER BY "CreatedAt",
        // asi que el indice compuesto cubre filtro y orden de una sola pasada.
        // ---------------------------------------------------------------------
        builder.HasIndex(c => new { c.ReviewId, c.CreatedAt });

        // Necesario para las consultas que parten de un nodo puntual (cargar respuestas
        // bajo demanda si en el futuro se pagina el hilo) y para que el FK auto-referenciado
        // no obligue a un seq scan al validar la integridad.
        builder.HasIndex(c => c.ParentCommentId);

        // Perfil publico y timeline: comentarios de un usuario, mas recientes primero.
        builder.HasIndex(c => new { c.UserId, c.CreatedAt })
            .IsDescending(false, true);

        builder.HasOne(c => c.Review)
            .WithMany(r => r.Comments)
            .HasForeignKey(c => c.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, no Cascade: borrar un usuario no debe arrastrar comentarios que
        // sostienen respuestas de terceros. La baja de cuenta se resuelve anonimizando.
        builder.HasOne(c => c.User)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Auto-referencia. Restrict porque el borrado de comentarios es logico
        // (IsDeleted): un nodo intermedio nunca desaparece fisicamente, asi que
        // nunca deja huerfanas a sus respuestas.
        builder.HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
