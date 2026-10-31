using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational.Configurations;

internal sealed class PanelStateConfiguration : IEntityTypeConfiguration<PanelState>
{
    public void Configure(EntityTypeBuilder<PanelState> builder)
    {
        builder.ToTable("_state_");

        builder.HasKey(state => state.Key);
        builder.Property(state => state.Key).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(state => state.Version).IsRequired();
        builder.Property(state => state.UpdatedAt).IsRequired();
    }
}
