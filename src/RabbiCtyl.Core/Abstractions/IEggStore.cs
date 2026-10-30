using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

/// <summary>
/// Game templates ("eggs"). A server is created from a template, which supplies
/// the startup command, container image, install script and default variables.
/// </summary>
public interface IEggStore
{
    Task<IReadOnlyList<Egg>> ListAsync(CancellationToken cancellationToken = default);

    Task<Egg?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Egg?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task AddAsync(Egg egg, CancellationToken cancellationToken = default);

    Task UpdateAsync(Egg egg, CancellationToken cancellationToken = default);

    Task DeleteAsync(Egg egg, CancellationToken cancellationToken = default);
}
