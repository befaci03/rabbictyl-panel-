using Cassandra;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraServerStore : CassandraStoreBase, IServerStore
{
    public CassandraServerStore(ISession session, string keyspace)
        : base(session, keyspace)
    {
    }

    public async Task<IReadOnlyList<Server>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(
            $"SELECT data FROM {Table("servers_by_owner")} WHERE owner_id = ?",
            userId);

        return rows
            .Select(row => Deserialize<Server>(row.GetValue<string>("data")))
            .OrderBy(server => server.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<Server>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("servers")}");

        return rows
            .Select(row => Deserialize<Server>(row.GetValue<string>("data")))
            .OrderBy(server => server.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<Server>> ListPagedAsync(
        int page,
        int perPage,
        CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken))
            .Skip((Math.Max(page, 1) - 1) * perPage)
            .Take(perPage)
            .ToList();

    public async Task<Server?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("servers")} WHERE id = ?", id);

        return rows.FirstOrDefault() is { } row
            ? Deserialize<Server>(row.GetValue<string>("data"))
            : null;
    }

    public async Task<Server?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        var lookup = await QueryAsync(
            $"SELECT id FROM {Table("servers_by_uuid")} WHERE server_uuid = ?",
            uuid);

        return lookup.FirstOrDefault() is { } row
            ? await FindByIdAsync(row.GetValue<Guid>("id"), cancellationToken)
            : null;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT id FROM {Table("servers")}");

        return rows.Count();
    }

    public async Task AddAsync(Server server, CancellationToken cancellationToken = default) =>
        await UpsertAsync(server);

    public async Task UpdateAsync(Server server, CancellationToken cancellationToken = default)
    {
        server.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertAsync(server);
    }

    public async Task DeleteAsync(Server server, CancellationToken cancellationToken = default)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("servers")} WHERE id = ?", server.Id));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("servers_by_owner")} WHERE owner_id = ? AND id = ?",
            server.OwnerId, server.Id));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("servers_by_uuid")} WHERE server_uuid = ?", server.Uuid));

        await Session.ExecuteAsync(batch);
    }

    private async Task UpsertAsync(Server server)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("servers")} (id, owner_id, data) VALUES (?, ?, ?)",
            server.Id, server.OwnerId, Serialize(server)));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("servers_by_owner")} (owner_id, id, data) VALUES (?, ?, ?)",
            server.OwnerId, server.Id, Serialize(server)));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("servers_by_uuid")} (server_uuid, id) VALUES (?, ?)",
            server.Uuid, server.Id));

        await Session.ExecuteAsync(batch);
    }
}
