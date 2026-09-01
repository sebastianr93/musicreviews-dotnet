using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Persistence.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.Bio)
            .HasMaxLength(ApplicationUser.BioMaxLength);

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(ApplicationUser.AvatarUrlMaxLength);

        builder.Property(u => u.CreatedAt)
            .IsRequired();
    }
}
