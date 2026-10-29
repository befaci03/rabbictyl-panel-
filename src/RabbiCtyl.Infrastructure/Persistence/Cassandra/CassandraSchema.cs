using System.Text.RegularExpressions;

namespace RabbiCtyl.Infrastructure.Persistence.Cassandra;

internal static partial class CassandraSchema
{
    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyspacePattern();

    public static IReadOnlyList<string> Statements(string keyspace)
    {
        if (!KeyspacePattern().IsMatch(keyspace))
        {
            throw new InvalidOperationException(
                $"Invalid Cassandra keyspace '{keyspace}'. Use lowercase letters, digits and underscores.");
        }

        return
        [
            $"CREATE KEYSPACE IF NOT EXISTS {keyspace} " +
            "WITH replication = { 'class': 'SimpleStrategy', 'replication_factor': 1 }",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.users (" +
            "id uuid PRIMARY KEY, data text)",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.users_by_login (" +
            "login text PRIMARY KEY, id uuid)",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.servers (" +
            "id uuid PRIMARY KEY, owner_id uuid, data text)",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.servers_by_owner (" +
            "owner_id uuid, id uuid, data text, PRIMARY KEY (owner_id, id))",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.servers_by_uuid (" +
            "server_uuid uuid PRIMARY KEY, id uuid)",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.nodes (" +
            "id uuid PRIMARY KEY, data text)",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.nodes_by_uuid (" +
            "node_uuid uuid PRIMARY KEY, id uuid)",

            $"CREATE TABLE IF NOT EXISTS {keyspace}.nodes_by_token (" +
            "daemon_token_id text PRIMARY KEY, id uuid)",
        ];
    }
}
