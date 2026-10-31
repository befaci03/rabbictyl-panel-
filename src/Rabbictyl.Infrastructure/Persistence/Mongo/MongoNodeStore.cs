using MongoDB.Driver;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

public sealed class MongoNodeStore : INodeStore
{
    private readonly IMongoCollection<Node> _nodes;

    public MongoNodeStore(IMongoDatabase database)
    {
        MongoMappings.EnsureRegistered();
        _nodes = database.GetCollection<Node>("nodes");
    }

    public async Task<IReadOnlyList<Node>> ListAsync(CancellationToken cancellationToken = default) =>
        await _nodes.Find(FilterDefinition<Node>.Empty)
            .SortBy(node => node.Name)
            .ToListAsync(cancellationToken);

    public async Task<Node?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _nodes.Find(node => node.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<Node?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        await _nodes.Find(node => node.Uuid == uuid).FirstOrDefaultAsync(cancellationToken);

    public async Task<Node?> FindByDaemonTokenIdAsync(
        string daemonTokenId,
        CancellationToken cancellationToken = default) =>
        await _nodes.Find(node => node.DaemonTokenId == daemonTokenId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        (int)await _nodes.CountDocumentsAsync(FilterDefinition<Node>.Empty, cancellationToken: cancellationToken);

    public Task AddAsync(Node node, CancellationToken cancellationToken = default) =>
        _nodes.InsertOneAsync(node, cancellationToken: cancellationToken);

    public async Task UpdateAsync(Node node, CancellationToken cancellationToken = default)
    {
        node.UpdatedAt = DateTimeOffset.UtcNow;
        await _nodes.ReplaceOneAsync(item => item.Id == node.Id, node, cancellationToken: cancellationToken);
    }

    public Task DeleteAsync(Node node, CancellationToken cancellationToken = default) =>
        _nodes.DeleteOneAsync(item => item.Id == node.Id, cancellationToken);
}
