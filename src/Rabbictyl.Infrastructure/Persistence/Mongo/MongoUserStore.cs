using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

public sealed class MongoUserStore : IUserStore
{
    private readonly IMongoCollection<User> _users;

    public MongoUserStore(IMongoDatabase database)
    {
        MongoMappings.EnsureRegistered();
        _users = database.GetCollection<User>("users");
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _users.Find(user => user.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        var escaped = Regex.Escape(login.Trim());
        var pattern = new BsonRegularExpression($"^{escaped}$", "i");

        var filter = Builders<User>.Filter.Or(
            Builders<User>.Filter.Regex(user => user.Email, pattern),
            Builders<User>.Filter.Regex(user => user.Username, pattern));

        return await _users.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        await _users.Find(FilterDefinition<User>.Empty)
            .SortBy(user => user.Username)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        (int)await _users.CountDocumentsAsync(FilterDefinition<User>.Empty, cancellationToken: cancellationToken);

    public Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        _users.InsertOneAsync(user, cancellationToken: cancellationToken);

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _users.ReplaceOneAsync(item => item.Id == user.Id, user, cancellationToken: cancellationToken);
    }

    public Task DeleteAsync(User user, CancellationToken cancellationToken = default) =>
        _users.DeleteOneAsync(item => item.Id == user.Id, cancellationToken);
}
