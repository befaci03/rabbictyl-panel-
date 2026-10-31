using Cassandra;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraAllocationStore : CassandraStoreBase, IAllocationStore
{
    public CassandraAllocationStore(ISession session, string keyspace)
        : base(session, keyspace)
    {
    }

    public async Task<IReadOnlyList<Allocation>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("allocations")}");

        return rows
            .Select(row => Deserialize<Allocation>(row.GetValue<string>("data")))
            .OrderBy(allocation => allocation.Port)
            .ToList();
    }

    public async Task<IReadOnlyList<Allocation>> ListForNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(
            $"SELECT data FROM {Table("allocations_by_node")} WHERE node_id = ?",
            nodeId);

        return rows
            .Select(row => Deserialize<Allocation>(row.GetValue<string>("data")))
            .OrderBy(allocation => allocation.Port)
            .ToList();
    }

    public async Task<Allocation?> FindAsync(
        Guid nodeId,
        string ip,
        int port,
        CancellationToken cancellationToken = default)
    {
        var lookup = await QueryAsync(
            $"SELECT id FROM {Table("allocations_by_address")} WHERE node_id = ? AND ip = ? AND port = ?",
            nodeId,
            ip,
            port);

        if (lookup.FirstOrDefault() is not { } row)
        {
            return null;
        }

        var id = row.GetValue<Guid>("id");
        var stored = await QueryAsync($"SELECT data FROM {Table("allocations")} WHERE id = ?", id);

        return stored.FirstOrDefault() is { } document
            ? Deserialize<Allocation>(document.GetValue<string>("data"))
            : null;
    }

    public async Task AddRangeAsync(
        IEnumerable<Allocation> allocations,
        CancellationToken cancellationToken = default)
    {
        foreach (var allocation in allocations)
        {
            await UpsertAsync(allocation);
        }
    }

    public async Task UpdateAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        allocation.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertAsync(allocation);
    }

    public async Task DeleteAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement($"DELETE FROM {Table("allocations")} WHERE id = ?", allocation.Id));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("allocations_by_node")} WHERE node_id = ? AND id = ?",
            allocation.NodeId,
            allocation.Id));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("allocations_by_address")} WHERE node_id = ? AND ip = ? AND port = ?",
            allocation.NodeId,
            allocation.Ip,
            allocation.Port));

        await Session.ExecuteAsync(batch);
    }

    private async Task UpsertAsync(Allocation allocation)
    {
        var batch = new BatchStatement();
        var json = Serialize(allocation);

        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("allocations")} (id, data) VALUES (?, ?)",
            allocation.Id,
            json));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("allocations_by_node")} (node_id, id, data) VALUES (?, ?, ?)",
            allocation.NodeId,
            allocation.Id,
            json));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("allocations_by_address")} (node_id, ip, port, id) VALUES (?, ?, ?, ?)",
            allocation.NodeId,
            allocation.Ip,
            allocation.Port,
            allocation.Id));

        await Session.ExecuteAsync(batch);
    }
}
