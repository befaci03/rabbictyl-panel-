using Microsoft.EntityFrameworkCore;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Persistence.Relational;

public sealed class PanelDbContext : DbContext
{
    public PanelDbContext(DbContextOptions<PanelDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Server> Servers => Set<Server>();

    public DbSet<Node> Nodes => Set<Node>();

    public DbSet<Allocation> Allocations => Set<Allocation>();

    public DbSet<Egg> Eggs => Set<Egg>();

    public DbSet<EggVariable> EggVariables => Set<EggVariable>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PanelDbContext).Assembly);
    }
}
