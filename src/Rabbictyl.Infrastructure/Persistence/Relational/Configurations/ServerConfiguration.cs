using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> builder)
    {
        builder.ToTable("servers");

        builder.HasKey(server => server.Id);
        builder.Property(server => server.Id).ValueGeneratedNever();

        builder.Property(server => server.Uuid).IsRequired();
        builder.HasIndex(server => server.Uuid).IsUnique();

        builder.Property(server => server.Name).HasMaxLength(191).IsRequired();
        builder.Property(server => server.Description).HasMaxLength(1024);
        builder.Property(server => server.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(server => server.StartupCommand).HasMaxLength(2048).IsRequired();
        builder.Property(server => server.Image).HasMaxLength(255).IsRequired();
        builder.Property(server => server.Threads).HasMaxLength(64);
        builder.Property(server => server.AllocationIp).HasMaxLength(45).IsRequired();
        builder.Property(server => server.EggUuid).HasMaxLength(64).IsRequired();
        builder.HasIndex(server => server.NodeId);

        builder.HasOne(server => server.Owner)
            .WithMany(user => user.Servers)
            .HasForeignKey(server => server.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(server => server.Node)
            .WithMany(node => node.Servers)
            .HasForeignKey(server => server.NodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(server => server.Egg)
            .WithMany()
            .HasForeignKey(server => server.EggId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(server => server.OwnerId);
    }
}
