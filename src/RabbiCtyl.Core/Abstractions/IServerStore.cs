using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

public interface IServerStore
{
    Task<IReadOnlyList<Server>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Server>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Server>> ListPagedAsync(int page, int perPage, CancellationToken cancellationToken = default);

    Task<Server?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Server?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Server server, CancellationToken cancellationToken = default);

    Task UpdateAsync(Server server, CancellationToken cancellationToken = default);

    Task DeleteAsync(Server server, CancellationToken cancellationToken = default);
}
