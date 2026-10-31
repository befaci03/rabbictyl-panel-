using Microsoft.EntityFrameworkCore;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational;

public sealed class EfAllocationStore : IAllocationStore
{
    private readonly PanelDbContext _db;

    public EfAllocationStore(PanelDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Allocation>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Allocations
            .AsNoTracking()
            .OrderBy(allocation => allocation.Port)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Allocation>> ListForNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default) =>
        await _db.Allocations
            .AsNoTracking()
            .Where(allocation => allocation.NodeId == nodeId)
            .OrderBy(allocation => allocation.Port)
            .ToListAsync(cancellationToken);

    public Task<Allocation?> FindAsync(
        Guid nodeId,
        string ip,
        int port,
        CancellationToken cancellationToken = default) =>
        _db.Allocations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                allocation => allocation.NodeId == nodeId && allocation.Ip == ip && allocation.Port == port,
                cancellationToken);

    public async Task AddRangeAsync(
        IEnumerable<Allocation> allocations,
        CancellationToken cancellationToken = default)
    {
        _db.Allocations.AddRange(allocations);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        allocation.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Allocations.Update(allocation);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        _db.Allocations.Remove(allocation);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
