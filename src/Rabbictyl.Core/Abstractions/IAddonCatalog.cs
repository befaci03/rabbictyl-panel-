using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Core.Abstractions;

public interface IAddonCatalog
{
    Task<IReadOnlyList<AddonDescriptor>> ListAsync(bool enabledOnly = false, CancellationToken cancellationToken = default);

    Task<AddonDescriptor?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task<ExtensionAsset?> FindAssetAsync(string id, string assetId, CancellationToken cancellationToken = default);
}
