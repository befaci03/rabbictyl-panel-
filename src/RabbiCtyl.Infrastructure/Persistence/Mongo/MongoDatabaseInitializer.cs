using MongoDB.Driver;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Persistence.Mongo;

public sealed class MongoDatabaseInitializer : IDatabaseInitializer
{
    private static readonly Collation CaseInsensitive = new("en", strength: CollationStrength.Secondary);

    private readonly IMongoDatabase _database;

    public MongoDatabaseInitializer(IMongoDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        MongoMappings.EnsureRegistered();

        await CreateUniqueIndexAsync<User>(
            "users", "ux_users_email", user => user.Email, cancellationToken);

        await CreateUniqueIndexAsync<User>(
            "users", "ux_users_username", user => user.Username, cancellationToken);

        await _database.GetCollection<Server>("servers").Indexes.CreateOneAsync(
            new CreateIndexModel<Server>(
                Builders<Server>.IndexKeys.Ascending(server => server.OwnerId),
                new CreateIndexOptions { Name = "ix_servers_owner" }),
            cancellationToken: cancellationToken);

        await _database.GetCollection<Server>("servers").Indexes.CreateOneAsync(
            new CreateIndexModel<Server>(
                Builders<Server>.IndexKeys.Ascending(server => server.Uuid),
                new CreateIndexOptions { Name = "ux_servers_uuid", Unique = true }),
            cancellationToken: cancellationToken);

        await _database.GetCollection<Node>("nodes").Indexes.CreateOneAsync(
            new CreateIndexModel<Node>(
                Builders<Node>.IndexKeys.Ascending(node => node.DaemonTokenId),
                new CreateIndexOptions { Name = "ux_nodes_token_id", Unique = true }),
            cancellationToken: cancellationToken);

        await _database.GetCollection<Node>("nodes").Indexes.CreateOneAsync(
            new CreateIndexModel<Node>(
                Builders<Node>.IndexKeys.Ascending(node => node.Name),
                new CreateIndexOptions { Name = "ix_nodes_name" }),
            cancellationToken: cancellationToken);
    }

    private Task<string> CreateUniqueIndexAsync<TEntity>(
        string collectionName,
        string indexName,
        System.Linq.Expressions.Expression<Func<TEntity, object?>> field,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var collection = _database.GetCollection<TEntity>(collectionName);
        var model = new CreateIndexModel<TEntity>(
            Builders<TEntity>.IndexKeys.Ascending(field),
            new CreateIndexOptions { Name = indexName, Unique = true, Collation = CaseInsensitive });

        return collection.Indexes.CreateOneAsync(model, cancellationToken: cancellationToken);
    }
}
