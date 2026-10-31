using Cassandra;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraUserStore : CassandraStoreBase, IUserStore
{
    public CassandraUserStore(ISession session, string keyspace)
        : base(session, keyspace)
    {
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("users")} WHERE id = ?", id);
        var row = rows.FirstOrDefault();

        return row is null ? null : Deserialize<User>(row.GetValue<string>("data"));
    }

    public async Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        var normalized = login.Trim().ToLowerInvariant();

        var rows = await QueryAsync(
            $"SELECT id FROM {Table("users_by_login")} WHERE login IN (?, ?)",
            EmailKey(normalized),
            UsernameKey(normalized));

        var row = rows.FirstOrDefault();

        return row is null ? null : await FindByIdAsync(row.GetValue<Guid>("id"), cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("users")}");

        return rows
            .Select(row => Deserialize<User>(row.GetValue<string>("data")))
            .OrderBy(user => user.Username)
            .ToList();
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT id FROM {Table("users")}");

        return rows.Count();
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var batch = new BatchStatement().SetBatchType(BatchType.Logged)
            .Add(new SimpleStatement(
                $"INSERT INTO {Table("users")} (id, data) VALUES (?, ?)",
                user.Id,
                Serialize(user)))
            .Add(new SimpleStatement(
                $"INSERT INTO {Table("users_by_login")} (login, id) VALUES (?, ?)",
                EmailKey(user.Email),
                user.Id))
            .Add(new SimpleStatement(
                $"INSERT INTO {Table("users_by_login")} (login, id) VALUES (?, ?)",
                UsernameKey(user.Username),
                user.Id));

        await Session.ExecuteAsync(batch);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var existing = await FindByIdAsync(user.Id, cancellationToken);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var batch = new BatchStatement().SetBatchType(BatchType.Logged)
            .Add(new SimpleStatement(
                $"INSERT INTO {Table("users")} (id, data) VALUES (?, ?)",
                user.Id,
                Serialize(user)));

        if (existing is not null && !string.Equals(existing.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            batch.Add(new SimpleStatement(
                $"DELETE FROM {Table("users_by_login")} WHERE login = ?",
                EmailKey(existing.Email)));
        }

        if (existing is not null && !string.Equals(existing.Username, user.Username, StringComparison.OrdinalIgnoreCase))
        {
            batch.Add(new SimpleStatement(
                $"DELETE FROM {Table("users_by_login")} WHERE login = ?",
                UsernameKey(existing.Username)));
        }

        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("users_by_login")} (login, id) VALUES (?, ?)",
            EmailKey(user.Email),
            user.Id));

        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("users_by_login")} (login, id) VALUES (?, ?)",
            UsernameKey(user.Username),
            user.Id));

        await Session.ExecuteAsync(batch);
    }

    public async Task DeleteAsync(User user, CancellationToken cancellationToken = default)
    {
        var existing = await FindByIdAsync(user.Id, cancellationToken) ?? user;

        var batch = new BatchStatement().SetBatchType(BatchType.Logged)
            .Add(new SimpleStatement(
                $"DELETE FROM {Table("users")} WHERE id = ?",
                user.Id))
            .Add(new SimpleStatement(
                $"DELETE FROM {Table("users_by_login")} WHERE login = ?",
                EmailKey(existing.Email)))
            .Add(new SimpleStatement(
                $"DELETE FROM {Table("users_by_login")} WHERE login = ?",
                UsernameKey(existing.Username)));

        await Session.ExecuteAsync(batch);
    }

    private static string EmailKey(string email) => "email:" + email.Trim().ToLowerInvariant();

    private static string UsernameKey(string username) => "username:" + username.Trim().ToLowerInvariant();
}
