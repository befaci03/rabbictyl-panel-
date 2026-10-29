using System.Text.Json.Serialization;

namespace RabbiCtyl.Core.Entities;

public sealed class Node : Entity
{
    public Guid Uuid { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public string Fqdn { get; set; } = string.Empty;

    public bool UseSsl { get; set; } = true;

    public string Scheme => UseSsl ? "https" : "http";

    public string DaemonBaseUrl => $"{Scheme}://{Fqdn}:{DaemonPort}";

    public int DaemonPort { get; set; } = 8080;

    public int DaemonSftpPort { get; set; } = 2022;

    public bool BehindProxy { get; set; }

    public bool MaintenanceMode { get; set; }

    public bool Public { get; set; } = true;

    public int MemoryLimitMb { get; set; }

    public int MemoryOverallocatePercent { get; set; }

    public int DiskLimitMb { get; set; }

    public int DiskOverallocatePercent { get; set; }

    public int UploadSizeMb { get; set; } = 100;

    public string DaemonBase { get; set; } = "/var/lib/pterodactyl/volumes";

    /// <summary>Token id half of the <c>Bearer id.token</c> panel callback credential.</summary>
    public string DaemonTokenId { get; set; } = string.Empty;

    /// <summary>
    /// Daemon secret. This is the <c>token</c> in pats' config, the Bearer value
    /// for daemon API calls, and the HS256 signing key for temporary JWTs.
    /// </summary>
    public string DaemonToken { get; set; } = string.Empty;

    [JsonIgnore]
    public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();

    [JsonIgnore]
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}
