using MongoDB.Driver;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

public sealed class MongoServerStore : IServerStore
{
    private readonly IMongoCollection<Server> _servers;

    public MongoServerStore(IMongoDatabase database)
    {
        MongoMappings.EnsureRegistered();
        _servers = database.GetCollection<Server>("servers");
    }

    public async Task<IReadOnlyList<Server>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _servers.Find(server => server.OwnerId == userId)
            .SortBy(server => server.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Server>> ListAsync(CancellationToken cancellationToken = default) =>
        await _servers.Find(FilterDefinition<Server>.Empty)
            .SortBy(server => server.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Server>> ListPagedAsync(
        int page,
        int perPage,
        CancellationToken cancellationToken = default) =>
        await _servers.Find(FilterDefinition<Server>.Empty)
            .SortBy(server => server.Name)
            .Skip((Math.Max(page, 1) - 1) * perPage)
            .Limit(perPage)
            .ToListAsync(cancellationToken);

    public async Task<Server?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _servers.Find(server => server.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<Server?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        await _servers.Find(server => server.Uuid == uuid).FirstOrDefaultAsync(cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        (int)await _servers.CountDocumentsAsync(FilterDefinition<Server>.Empty, cancellationToken: cancellationToken);

    public Task AddAsync(Server server, CancellationToken cancellationToken = default) =>
        _servers.InsertOneAsync(server, cancellationToken: cancellationToken);

    public async Task UpdateAsync(Server server, CancellationToken cancellationToken = default)
    {
        server.UpdatedAt = DateTimeOffset.UtcNow;
        await _servers.ReplaceOneAsync(
            item => item.Id == server.Id,
            server,
            cancellationToken: cancellationToken);
    }

    public Task DeleteAsync(Server server, CancellationToken cancellationToken = default) =>
        _servers.DeleteOneAsync(item => item.Id == server.Id, cancellationToken);
}
