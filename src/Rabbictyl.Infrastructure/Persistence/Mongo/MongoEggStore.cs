using MongoDB.Driver;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

public sealed class MongoEggStore : IEggStore
{
    private readonly IMongoCollection<Egg> _eggs;

    public MongoEggStore(IMongoDatabase database)
    {
        MongoMappings.EnsureRegistered();
        _eggs = database.GetCollection<Egg>("eggs");
    }

    public async Task<IReadOnlyList<Egg>> ListAsync(CancellationToken cancellationToken = default) =>
        await _eggs.Find(FilterDefinition<Egg>.Empty)
            .SortBy(egg => egg.Name)
            .ToListAsync(cancellationToken);

    public Task<Egg?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _eggs.Find(egg => egg.Id == id).FirstOrDefaultAsync(cancellationToken)!;

    public Task<Egg?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default) =>
        _eggs.Find(egg => egg.Uuid == uuid).FirstOrDefaultAsync(cancellationToken)!;

    public Task AddAsync(Egg egg, CancellationToken cancellationToken = default) =>
        _eggs.InsertOneAsync(egg, cancellationToken: cancellationToken);

    public async Task UpdateAsync(Egg egg, CancellationToken cancellationToken = default)
    {
        egg.UpdatedAt = DateTimeOffset.UtcNow;
        await _eggs.ReplaceOneAsync(
            item => item.Id == egg.Id,
            egg,
            cancellationToken: cancellationToken);
    }

    public Task DeleteAsync(Egg egg, CancellationToken cancellationToken = default) =>
        _eggs.DeleteOneAsync(item => item.Id == egg.Id, cancellationToken);
}
