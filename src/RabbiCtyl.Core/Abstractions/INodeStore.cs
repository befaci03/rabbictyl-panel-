using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

public interface INodeStore
{
    Task<IReadOnlyList<Node>> ListAsync(CancellationToken cancellationToken = default);

    Task<Node?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Node?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task<Node?> FindByDaemonTokenIdAsync(string daemonTokenId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Node node, CancellationToken cancellationToken = default);

    Task UpdateAsync(Node node, CancellationToken cancellationToken = default);

    Task DeleteAsync(Node node, CancellationToken cancellationToken = default);
}
