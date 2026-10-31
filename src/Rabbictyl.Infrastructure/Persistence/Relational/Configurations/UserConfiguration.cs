using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.Username).HasMaxLength(191).IsRequired();
        builder.HasIndex(user => user.Username).IsUnique();

        builder.Property(user => user.Email).HasMaxLength(191).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.NameFirst).HasMaxLength(191).IsRequired();
        builder.Property(user => user.NameLast).HasMaxLength(191).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(user => user.TotpSecret).HasMaxLength(255);
        builder.Property(user => user.Language).HasMaxLength(8).IsRequired();
    }
}
