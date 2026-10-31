using Cassandra;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraDatabaseInitializer : IDatabaseInitializer
{
    private readonly ISession _session;
    private readonly string _keyspace;

    public CassandraDatabaseInitializer(ISession session, string keyspace)
    {
        _session = session;
        _keyspace = keyspace;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var statement in CassandraSchema.Statements(_keyspace))
        {
            await _session.ExecuteAsync(new SimpleStatement(statement));
        }
    }
}
