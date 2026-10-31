using MongoDB.Driver;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

public sealed class MongoAllocationStore : IAllocationStore
{
    private readonly IMongoCollection<Allocation> _allocations;

    public MongoAllocationStore(IMongoDatabase database)
    {
        MongoMappings.EnsureRegistered();
        _allocations = database.GetCollection<Allocation>("allocations");
    }

    public async Task<IReadOnlyList<Allocation>> ListAsync(CancellationToken cancellationToken = default) =>
        await _allocations.Find(FilterDefinition<Allocation>.Empty)
            .SortBy(allocation => allocation.Port)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Allocation>> ListForNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default) =>
        await _allocations.Find(allocation => allocation.NodeId == nodeId)
            .SortBy(allocation => allocation.Port)
            .ToListAsync(cancellationToken);

    public Task<Allocation?> FindAsync(
        Guid nodeId,
        string ip,
        int port,
        CancellationToken cancellationToken = default) =>
        _allocations
            .Find(allocation => allocation.NodeId == nodeId && allocation.Ip == ip && allocation.Port == port)
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task AddRangeAsync(
        IEnumerable<Allocation> allocations,
        CancellationToken cancellationToken = default) =>
        _allocations.InsertManyAsync(allocations, cancellationToken: cancellationToken);

    public async Task UpdateAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        allocation.UpdatedAt = DateTimeOffset.UtcNow;
        await _allocations.ReplaceOneAsync(
            item => item.Id == allocation.Id,
            allocation,
            cancellationToken: cancellationToken);
    }

    public Task DeleteAsync(Allocation allocation, CancellationToken cancellationToken = default) =>
        _allocations.DeleteOneAsync(item => item.Id == allocation.Id, cancellationToken);
}
