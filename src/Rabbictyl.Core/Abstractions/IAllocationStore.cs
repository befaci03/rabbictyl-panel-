using Rabbictyl.Core.Entities;

namespace Rabbictyl.Core.Abstractions;

public interface IAllocationStore
{
    Task<IReadOnlyList<Allocation>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Allocation>> ListForNodeAsync(Guid nodeId, CancellationToken cancellationToken = default);

    Task<Allocation?> FindAsync(
        Guid nodeId,
        string ip,
        int port,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<Allocation> allocations, CancellationToken cancellationToken = default);

    Task UpdateAsync(Allocation allocation, CancellationToken cancellationToken = default);

    Task DeleteAsync(Allocation allocation, CancellationToken cancellationToken = default);
}
