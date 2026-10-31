using Microsoft.EntityFrameworkCore;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational;

public sealed class EfEggStore : IEggStore
{
    private readonly PanelDbContext _db;

    public EfEggStore(PanelDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Egg>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Eggs
            .AsNoTracking()
            .OrderBy(egg => egg.Name)
            .ToListAsync(cancellationToken);

    public Task<Egg?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Eggs.AsNoTracking().FirstOrDefaultAsync(egg => egg.Id == id, cancellationToken);

    public Task<Egg?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        _db.Eggs.AsNoTracking().FirstOrDefaultAsync(egg => egg.Uuid == uuid, cancellationToken);

    public async Task AddAsync(Egg egg, CancellationToken cancellationToken = default)
    {
        _db.Eggs.Add(egg);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Egg egg, CancellationToken cancellationToken = default)
    {
        egg.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Eggs.Update(egg);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Egg egg, CancellationToken cancellationToken = default)
    {
        _db.Eggs.Remove(egg);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
