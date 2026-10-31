using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("allocations");

        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.Id).ValueGeneratedNever();

        builder.Property(allocation => allocation.Ip).HasMaxLength(45).IsRequired();
        builder.HasIndex(allocation => new { allocation.NodeId, allocation.Ip, allocation.Port }).IsUnique();

        builder.HasOne(allocation => allocation.Node)
            .WithMany(node => node.Allocations)
            .HasForeignKey(allocation => allocation.NodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(allocation => allocation.Server)
            .WithMany()
            .HasForeignKey(allocation => allocation.ServerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
