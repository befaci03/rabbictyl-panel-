using Cassandra;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraNodeStore : CassandraStoreBase, INodeStore
{
    public CassandraNodeStore(ISession session, string keyspace)
        : base(session, keyspace)
    {
    }

    public async Task<IReadOnlyList<Node>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("nodes")}");

        return rows
            .Select(row => Deserialize<Node>(row.GetValue<string>("data")))
            .OrderBy(node => node.Name)
            .ToList();
    }

    public async Task<Node?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("nodes")} WHERE id = ?", id);

        return rows.FirstOrDefault() is { } row
            ? Deserialize<Node>(row.GetValue<string>("data"))
            : null;
    }

    public async Task<Node?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        var lookup = await QueryAsync(
            $"SELECT id FROM {Table("nodes_by_uuid")} WHERE node_uuid = ?",
            uuid);

        return lookup.FirstOrDefault() is { } row
            ? await FindByIdAsync(row.GetValue<Guid>("id"), cancellationToken)
            : null;
    }

    public async Task<Node?> FindByDaemonTokenIdAsync(
        string daemonTokenId,
        CancellationToken cancellationToken = default)
    {
        var lookup = await QueryAsync(
            $"SELECT id FROM {Table("nodes_by_token")} WHERE daemon_token_id = ?",
            daemonTokenId);

        return lookup.FirstOrDefault() is { } row
            ? await FindByIdAsync(row.GetValue<Guid>("id"), cancellationToken)
            : null;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT id FROM {Table("nodes")}");

        return rows.Count();
    }

    public async Task AddAsync(Node node, CancellationToken cancellationToken = default) =>
        await UpsertAsync(node);

    public async Task UpdateAsync(Node node, CancellationToken cancellationToken = default)
    {
        node.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertAsync(node);
    }

    public async Task DeleteAsync(Node node, CancellationToken cancellationToken = default)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("nodes")} WHERE id = ?", node.Id));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("nodes_by_uuid")} WHERE node_uuid = ?", node.Uuid));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("nodes_by_token")} WHERE daemon_token_id = ?", node.DaemonTokenId));

        await Session.ExecuteAsync(batch);
    }

    private async Task UpsertAsync(Node node)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("nodes")} (id, data) VALUES (?, ?)",
            node.Id, Serialize(node)));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("nodes_by_uuid")} (node_uuid, id) VALUES (?, ?)",
            node.Uuid, node.Id));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("nodes_by_token")} (daemon_token_id, id) VALUES (?, ?)",
            node.DaemonTokenId, node.Id));

        await Session.ExecuteAsync(batch);
    }
}
