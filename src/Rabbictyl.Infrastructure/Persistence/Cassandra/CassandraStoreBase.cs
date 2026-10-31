using System.Text.Json;
using System.Text.Json.Serialization;
using Cassandra;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

/// <summary>
/// Cassandra is used as a document-ish store: entities are JSON encoded into a
/// single <c>data</c> column while dedicated lookup tables cover the queries we
/// actually need. This keeps the domain model free of provider specific types.
/// </summary>
public abstract class CassandraStoreBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    protected CassandraStoreBase(ISession session, string keyspace)
    {
        Session = session;
        Keyspace = keyspace;
    }

    protected ISession Session { get; }

    protected string Keyspace { get; }

    protected string Table(string name) => $"{Keyspace}.{name}";

    protected Task<RowSet> QueryAsync(string cql, params object[] values) =>
        Session.ExecuteAsync(new SimpleStatement(cql, values));

    protected static string Serialize<TEntity>(TEntity entity) =>
        JsonSerializer.Serialize(entity, JsonOptions);

    protected static TEntity Deserialize<TEntity>(string json) =>
        JsonSerializer.Deserialize<TEntity>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Could not deserialize a {typeof(TEntity).Name} document.");
}
