using Microsoft.EntityFrameworkCore;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Relational;

public sealed class EfServerStore : IServerStore
{
    private readonly PanelDbContext _db;

    public EfServerStore(PanelDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Server>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await Query()
            .Where(server => server.OwnerId == userId)
            .OrderBy(server => server.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Server>> ListAsync(CancellationToken cancellationToken = default) =>
        await Query()
            .OrderBy(server => server.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Server>> ListPagedAsync(
        int page,
        int perPage,
        CancellationToken cancellationToken = default) =>
        await Query()
            .OrderBy(server => server.Name)
            .Skip((Math.Max(page, 1) - 1) * perPage)
            .Take(perPage)
            .ToListAsync(cancellationToken);

    public Task<Server?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Query().FirstOrDefaultAsync(server => server.Id == id, cancellationToken);

    public Task<Server?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        Query().FirstOrDefaultAsync(server => server.Uuid == uuid, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Servers.CountAsync(cancellationToken);

    public async Task AddAsync(Server server, CancellationToken cancellationToken = default)
    {
        _db.Servers.Add(server);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Server server, CancellationToken cancellationToken = default)
    {
        server.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Servers.Update(server);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Server server, CancellationToken cancellationToken = default)
    {
        _db.Servers.Remove(server);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<Server> Query() =>
        _db.Servers
            .AsNoTracking()
            .Include(server => server.Owner)
            .Include(server => server.Node)
            .Include(server => server.Egg);
}
