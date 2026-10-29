namespace RabbiCtyl.Core.Abstractions;

public interface IAddonState
{
    /// <summary>
    /// Returns the panel override for an addon, or <c>null</c> when the manifest
    /// value should be used.
    /// </summary>
    bool? GetOverride(string addonId);

    Task SetEnabledAsync(string addonId, bool enabled, CancellationToken cancellationToken = default);
}
