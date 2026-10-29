using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.HasKey(apiKey => apiKey.Id);
        builder.Property(apiKey => apiKey.Id).ValueGeneratedNever();

        builder.Property(apiKey => apiKey.Identifier).HasMaxLength(64).IsRequired();
        builder.HasIndex(apiKey => apiKey.Identifier).IsUnique();

        builder.Property(apiKey => apiKey.TokenHash).HasMaxLength(255).IsRequired();
        builder.Property(apiKey => apiKey.Memo).HasMaxLength(500);

        builder.HasOne(apiKey => apiKey.User)
            .WithMany()
            .HasForeignKey(apiKey => apiKey.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(apiKey => apiKey.UserId);
    }
}
