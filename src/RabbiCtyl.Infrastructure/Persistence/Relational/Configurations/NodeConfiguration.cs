using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class NodeConfiguration : IEntityTypeConfiguration<Node>
{
    public void Configure(EntityTypeBuilder<Node> builder)
    {
        builder.ToTable("nodes");

        builder.HasKey(node => node.Id);
        builder.Property(node => node.Id).ValueGeneratedNever();

        builder.Property(node => node.Uuid).IsRequired();
        builder.HasIndex(node => node.Uuid).IsUnique();

        builder.Property(node => node.Name).HasMaxLength(191).IsRequired();
        builder.Property(node => node.Fqdn).HasMaxLength(255).IsRequired();
        builder.Property(node => node.DaemonTokenId).HasMaxLength(64).IsRequired();
        builder.HasIndex(node => node.DaemonTokenId).IsUnique();
        builder.Property(node => node.DaemonToken).HasMaxLength(255).IsRequired();
        builder.Property(node => node.DaemonBase).HasMaxLength(255).IsRequired();
    }
}
