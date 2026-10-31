using Cassandra;
using Microsoft.Extensions.Logging;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraPanelStateStore : CassandraStoreBase, IPanelStateStore
{
    private readonly string _table;
    private readonly ILogger<CassandraPanelStateStore> _logger;

    public CassandraPanelStateStore(
        ISession session,
        string keyspace,
        ILogger<CassandraPanelStateStore> logger)
        : base(session, keyspace)
    {
        // Quoted because the table name intentionally starts with an underscore.
        _table = $"{keyspace}.\"_state_\"";
        _logger = logger;
    }

    public async Task<PanelState?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await QueryAsync($"SELECT data FROM {_table} WHERE key = ?", PanelState.SingletonKey);

            return rows.FirstOrDefault() is { } row
                ? Deserialize<PanelState>(row.GetValue<string>("data"))
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug(exception, "The _state_ table is not readable; assuming a fresh database.");

            return null;
        }
    }

    public async Task SaveAsync(PanelState state, CancellationToken cancellationToken = default) =>
        await QueryAsync(
            $"INSERT INTO {_table} (key, data) VALUES (?, ?)",
            state.Key,
            Serialize(state));
}
