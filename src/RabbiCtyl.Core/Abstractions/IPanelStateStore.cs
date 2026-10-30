using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

/// <summary>Reads and writes the <c>_state_</c> row that tracks the schema version.</summary>
public interface IPanelStateStore
{
    Task<PanelState?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PanelState state, CancellationToken cancellationToken = default);
}
