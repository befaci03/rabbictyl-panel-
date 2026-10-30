using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Core.Abstractions;

/// <summary>Reads the nest files that ship with (or are dropped into) the panel.</summary>
public interface INestCatalog
{
    Task<IReadOnlyList<NestDescriptor>> ListAsync(CancellationToken cancellationToken = default);
}
