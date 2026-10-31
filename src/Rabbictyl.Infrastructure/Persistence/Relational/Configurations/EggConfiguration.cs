using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class EggConfiguration : IEntityTypeConfiguration<Egg>
{
    public void Configure(EntityTypeBuilder<Egg> builder)
    {
        builder.ToTable("eggs");

        builder.HasKey(egg => egg.Id);
        builder.Property(egg => egg.Id).ValueGeneratedNever();

        builder.Property(egg => egg.Uuid).IsRequired();
        builder.HasIndex(egg => egg.Uuid).IsUnique();

        builder.Property(egg => egg.Name).HasMaxLength(191).IsRequired();
        builder.Property(egg => egg.Description).HasMaxLength(1024);
        builder.Property(egg => egg.Author).HasMaxLength(191).IsRequired();
        builder.Property(egg => egg.DockerImage).HasMaxLength(255).IsRequired();
        builder.Property(egg => egg.StartupCommand).HasMaxLength(1024).IsRequired();
        builder.Property(egg => egg.ScriptContainer).HasMaxLength(255).IsRequired();
        builder.Property(egg => egg.ScriptEntrypoint).HasMaxLength(255).IsRequired();

        // Variables live in the egg document so every persistence provider stores
        // the same shape; the navigation stays available for C# callers only.
        builder.Ignore(egg => egg.Variables);
    }
}
