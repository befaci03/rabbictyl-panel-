using Microsoft.Extensions.Logging;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Addons.Abstractions;

/// <summary>Everything an addon receives when the panel starts it.</summary>
public sealed class AddonContext
{
    public required AddonManifest Manifest { get; init; }

    public required string Directory { get; init; }

    /// <summary>Writable directory reserved for this addon.</summary>
    public required string StorageDirectory { get; init; }

    public required IServiceProvider Services { get; init; }

    public required ILogger Logger { get; init; }

    public string Id => Manifest.Id;
}
