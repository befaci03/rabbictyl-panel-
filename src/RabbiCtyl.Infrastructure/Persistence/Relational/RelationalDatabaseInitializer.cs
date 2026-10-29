using Microsoft.EntityFrameworkCore;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Infrastructure.Persistence.Relational;

public sealed class RelationalDatabaseInitializer : IDatabaseInitializer
{
    private readonly PanelDbContext _db;

    public RelationalDatabaseInitializer(PanelDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var migrations = _db.Database.GetMigrations();

        if (migrations.Any())
        {
            await _db.Database.MigrateAsync(cancellationToken);
            return;
        }

        // Until the first migration is generated we create the schema directly.
        // Once migrations exist the branch above takes over automatically.
        await _db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
