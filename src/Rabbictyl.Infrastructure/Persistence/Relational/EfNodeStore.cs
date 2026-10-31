using Microsoft.EntityFrameworkCore;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational;

public sealed class EfNodeStore : INodeStore
{
    private readonly PanelDbContext _db;

    public EfNodeStore(PanelDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Node>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Nodes
            .AsNoTracking()
            .OrderBy(node => node.Name)
            .ToListAsync(cancellationToken);

    public Task<Node?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Nodes.FirstOrDefaultAsync(node => node.Id == id, cancellationToken);

    public Task<Node?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        _db.Nodes.FirstOrDefaultAsync(node => node.Uuid == uuid, cancellationToken);

    public Task<Node?> FindByDaemonTokenIdAsync(
        string daemonTokenId,
        CancellationToken cancellationToken = default) =>
        _db.Nodes.FirstOrDefaultAsync(
            node => node.DaemonTokenId == daemonTokenId,
            cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Nodes.CountAsync(cancellationToken);

    public async Task AddAsync(Node node, CancellationToken cancellationToken = default)
    {
        _db.Nodes.Add(node);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Node node, CancellationToken cancellationToken = default)
    {
        node.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Nodes.Update(node);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Node node, CancellationToken cancellationToken = default)
    {
        _db.Nodes.Remove(node);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
