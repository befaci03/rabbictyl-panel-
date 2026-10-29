using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Core.Abstractions;

public interface IAddonCatalog
{
    Task<IReadOnlyList<AddonDescriptor>> ListAsync(bool enabledOnly = false, CancellationToken cancellationToken = default);

    Task<AddonDescriptor?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task<ExtensionAsset?> FindAssetAsync(string id, string assetId, CancellationToken cancellationToken = default);
}
