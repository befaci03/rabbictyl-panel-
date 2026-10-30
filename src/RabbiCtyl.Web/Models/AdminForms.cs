using System.ComponentModel.DataAnnotations;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Models;

public sealed class AdminNodesViewModel
{
    public IReadOnlyList<Node> Nodes { get; init; } = [];

    public string PanelUrl { get; init; } = string.Empty;
}

public sealed class CreateNodeForm
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(255, MinimumLength = 1)]
    public string Fqdn { get; set; } = string.Empty;

    public bool UseSsl { get; set; } = true;

    [Range(1, 65535)]
    public int DaemonPort { get; set; } = 8080;

    [Range(1, 65535)]
    public int DaemonSftpPort { get; set; } = 2022;

    public bool BehindProxy { get; set; }

    [Range(128, 1_048_576)]
    public int MemoryMb { get; set; } = 4096;

    [Range(0, 1000)]
    public int MemoryOverallocatePercent { get; set; }

    [Range(1024, 10_485_760)]
    public int DiskMb { get; set; } = 51200;

    [Range(0, 1000)]
    public int DiskOverallocatePercent { get; set; }

    [Range(1, 10_240)]
    public int UploadSizeMb { get; set; } = 100;

    [Required]
    public string DaemonBase { get; set; } = "/var/lib/pterodactyl/volumes";
}

public sealed class CreateServerForm
{
    [Required]
    public Guid NodeUuid { get; set; }

    [Required]
    public Guid OwnerId { get; set; }

    /// <summary>Optional game template; when set its defaults replace the manual startup fields.</summary>
    public Guid? EggUuid { get; set; }

    [Required, StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Description { get; set; }

    [Required, StringLength(64, MinimumLength = 1)]
    public string AllocationIp { get; set; } = "0.0.0.0";

    [Range(1, 65535)]
    public int AllocationPort { get; set; } = 25565;

    [Range(128, 1_048_576)]
    public int MemoryMb { get; set; } = 1024;

    [Range(0, 1_048_576)]
    public int DiskMb { get; set; } = 10240;

    [Range(0, 10_000)]
    public int CpuLimitPercent { get; set; } = 100;

    [StringLength(1024)]
    public string Startup { get; set; } = "java -Xms128M -Xmx1024M -jar server.jar";

    [StringLength(255)]
    public string Image { get; set; } = "ghcr.io/pterodactyl/yolks:java_21";
}

public sealed class CreateGameTemplateForm
{
    [Required, StringLength(191, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    [StringLength(191)]
    public string Author { get; set; } = string.Empty;

    [Required, StringLength(1024, MinimumLength = 1)]
    public string Startup { get; set; } = "java -Xms128M -Xmx1024M -jar server.jar";

    [Required, StringLength(255, MinimumLength = 1)]
    public string Image { get; set; } = "ghcr.io/pterodactyl/yolks:java_21";

    [StringLength(255)]
    public string ScriptContainer { get; set; } = "alpine:3.20";

    [StringLength(255)]
    public string ScriptEntrypoint { get; set; } = "ash";

    /// <summary>Install script body, run once after the container is created.</summary>
    public string? InstallScript { get; set; }

    /// <summary>One <c>ENV=default</c> pair per line.</summary>
    public string? Variables { get; set; }

    /// <summary>Startup detection patterns, one per line.</summary>
    public string? DonePatterns { get; set; }

    [StringLength(16)]
    public string StopType { get; set; } = "command";

    [StringLength(64)]
    public string StopValue { get; set; } = "stop";
}

public sealed class UpdateGameTemplateForm
{
    [Required, StringLength(191, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    [StringLength(191)]
    public string Author { get; set; } = string.Empty;

    [Required, StringLength(1024, MinimumLength = 1)]
    public string Startup { get; set; } = string.Empty;

    [Required, StringLength(255, MinimumLength = 1)]
    public string Image { get; set; } = string.Empty;
}

public sealed class CreateUserForm
{
    [Required, StringLength(32, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9_.-]+$")]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 1)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 1)]
    public string LastName { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    public bool IsRootAdmin { get; set; }
}
