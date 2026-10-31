using Cassandra;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Cassandra;

public sealed class CassandraEggStore : CassandraStoreBase, IEggStore
{
    public CassandraEggStore(ISession session, string keyspace)
        : base(session, keyspace)
    {
    }

    public async Task<IReadOnlyList<Egg>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("eggs")}");

        return rows
            .Select(row => Deserialize<Egg>(row.GetValue<string>("data")))
            .OrderBy(egg => egg.Name)
            .ToList();
    }

    public async Task<Egg?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync($"SELECT data FROM {Table("eggs")} WHERE id = ?", id);

        return rows.FirstOrDefault() is { } row
            ? Deserialize<Egg>(row.GetValue<string>("data"))
            : null;
    }

    public async Task<Egg?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        var lookup = await QueryAsync(
            $"SELECT id FROM {Table("eggs_by_uuid")} WHERE egg_uuid = ?",
            uuid);

        return lookup.FirstOrDefault() is { } row
            ? await FindByIdAsync(row.GetValue<Guid>("id"), cancellationToken)
            : null;
    }

    public async Task AddAsync(Egg egg, CancellationToken cancellationToken = default) =>
        await UpsertAsync(egg);

    public async Task UpdateAsync(Egg egg, CancellationToken cancellationToken = default)
    {
        egg.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertAsync(egg);
    }

    public async Task DeleteAsync(Egg egg, CancellationToken cancellationToken = default)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement($"DELETE FROM {Table("eggs")} WHERE id = ?", egg.Id));
        batch.Add(new SimpleStatement(
            $"DELETE FROM {Table("eggs_by_uuid")} WHERE egg_uuid = ?",
            egg.Uuid));

        await Session.ExecuteAsync(batch);
    }

    private async Task UpsertAsync(Egg egg)
    {
        var batch = new BatchStatement();

        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("eggs")} (id, data) VALUES (?, ?)",
            egg.Id, Serialize(egg)));
        batch.Add(new SimpleStatement(
            $"INSERT INTO {Table("eggs_by_uuid")} (egg_uuid, id) VALUES (?, ?)",
            egg.Uuid, egg.Id));

        await Session.ExecuteAsync(batch);
    }
}
