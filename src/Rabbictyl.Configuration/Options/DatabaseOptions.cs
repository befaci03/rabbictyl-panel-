namespace Rabbictyl.Configuration.Options;

public enum DatabaseProvider
{
    Sqlite,
    MySql,
    MariaDb,
    PostgreSql,
    MongoDb,
    Cassandra,
}

public sealed class DatabaseOptions
{
    /// <summary>
    /// One of: sqlite, mariadb, mysql, postgresql, mongodb, cassandra.
    /// </summary>
    public string Provider { get; set; } = "sqlite";

    public string ConnectionString { get; set; } = "Data Source=rabbictyl.db";

    /// <summary>Database name used by MongoDB.</summary>
    public string DatabaseName { get; set; } = "rabbictyl";

    /// <summary>Keyspace used by Cassandra.</summary>
    public string Keyspace { get; set; } = "rabbictyl";

    public DatabaseProvider ResolveProvider()
    {
        if (Enum.TryParse<DatabaseProvider>(Provider, ignoreCase: true, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{Provider}'. Expected one of: sqlite, mariadb, mysql, postgresql, mongodb, cassandra.");
    }

    public bool IsRelational => ResolveProvider() is
        DatabaseProvider.Sqlite or
        DatabaseProvider.MySql or
        DatabaseProvider.MariaDb or
        DatabaseProvider.PostgreSql;
}
