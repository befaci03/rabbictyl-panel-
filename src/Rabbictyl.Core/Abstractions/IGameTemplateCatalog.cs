using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Core.Abstractions;

/// <summary>
/// Every template a server can be created from, regardless of whether it was
/// loaded from a nest file or created in the admin area.
/// </summary>
public interface IGameTemplateCatalog
{
    Task<IReadOnlyList<GameTemplate>> ListAsync(CancellationToken cancellationToken = default);

    Task<GameTemplate?> FindAsync(Guid uuid, CancellationToken cancellationToken = default);
}
