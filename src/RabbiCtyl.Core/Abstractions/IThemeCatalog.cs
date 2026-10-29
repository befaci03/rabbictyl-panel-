using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Core.Abstractions;

public interface IThemeCatalog
{
    Task<IReadOnlyList<ThemeDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    Task<ThemeDescriptor?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task<ExtensionAsset?> FindAssetAsync(string id, string assetId, CancellationToken cancellationToken = default);
}
