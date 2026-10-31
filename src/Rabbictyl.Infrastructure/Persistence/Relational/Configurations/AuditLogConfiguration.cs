using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(auditLog => auditLog.Id);
        builder.Property(auditLog => auditLog.Id).ValueGeneratedNever();

        builder.Property(auditLog => auditLog.Action).HasMaxLength(191).IsRequired();
        builder.Property(auditLog => auditLog.IpAddress).HasMaxLength(45);
        builder.Property(auditLog => auditLog.MetadataJson).HasMaxLength(4000);

        builder.HasIndex(auditLog => auditLog.ActorId);
        builder.HasIndex(auditLog => auditLog.CreatedAt);
    }
}
