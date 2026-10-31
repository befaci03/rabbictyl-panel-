using MongoDB.Driver;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

public sealed class MongoPanelStateStore : IPanelStateStore
{
    private readonly IMongoCollection<PanelState> _states;

    public MongoPanelStateStore(IMongoDatabase database)
    {
        MongoMappings.EnsureRegistered();
        _states = database.GetCollection<PanelState>("_state_");
    }

    public Task<PanelState?> GetAsync(CancellationToken cancellationToken = default) =>
        _states.Find(state => state.Key == PanelState.SingletonKey)
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task SaveAsync(PanelState state, CancellationToken cancellationToken = default) =>
        _states.ReplaceOneAsync(
            item => item.Key == state.Key,
            state,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
}
