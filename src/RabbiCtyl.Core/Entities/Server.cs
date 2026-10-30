using System.Text.Json.Serialization;

namespace RabbiCtyl.Core.Entities;

public sealed class Server : Entity
{
    /// <summary>Pterodactyl/pats identify servers by a UUIDv4 string.</summary>
    public Guid Uuid { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid OwnerId { get; set; }

    [JsonIgnore]
    public User? Owner { get; set; }

    public Guid NodeId { get; set; }

    [JsonIgnore]
    public Node? Node { get; set; }

    public Guid? EggId { get; set; }

    [JsonIgnore]
    public Egg? Egg { get; set; }

    public ServerStatus Status { get; set; } = ServerStatus.Installing;

    public bool Suspended { get; set; }

    /// <summary>
    /// Temporary access restriction: the server keeps running, but its owner
    /// can only reach the console, files and server settings.
    /// </summary>
    public bool AccessRestricted { get; set; }

    public bool SkipScripts { get; set; }

    // Build limits, using Pterodactyl's names and units (MiB / percent).
    public int MemoryLimitMb { get; set; }

    public int SwapMb { get; set; }

    public int DiskLimitMb { get; set; }

    public int IoWeight { get; set; } = 500;

    public int CpuLimitPercent { get; set; }

    public string? Threads { get; set; }

    public bool OomDisabled { get; set; } = true;

    // Primary allocation and the full mapping pats exposes to the container.
    public string AllocationIp { get; set; } = "0.0.0.0";

    public int AllocationPort { get; set; }

    public string AllocationMappingsJson { get; set; } = "{}";

    // Runtime configuration snapshots rendered from the egg at create/update time.
    public string StartupCommand { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;

    public string EnvironmentJson { get; set; } = "{}";

    public string MountsJson { get; set; } = "[]";

    public string? DaemonConfigurationJson { get; set; }

    public string? ProcessConfigurationJson { get; set; }

    public string? InstallScriptJson { get; set; }

    // Egg metadata copied onto the server so the daemon contract never depends on
    // a provider-specific navigation load.
    public string EggUuid { get; set; } = string.Empty;

    public bool EggForceOutgoingIp { get; set; }

    public string EggFileDenylistJson { get; set; } = "[]";

    public DateTimeOffset? InstalledAt { get; set; }

    public int AllocationLimit { get; set; }

    public int DatabaseLimit { get; set; }

    public int BackupLimit { get; set; }
}
