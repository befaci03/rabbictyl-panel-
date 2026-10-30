using System.ComponentModel.DataAnnotations;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Web.Models;

public sealed class AdminServersViewModel
{
    public IReadOnlyList<AdminServerRow> Servers { get; init; } = [];

    public IReadOnlyList<Node> Nodes { get; init; } = [];

    public IReadOnlyList<User> Users { get; init; } = [];

    public IReadOnlyList<GameTemplate> Eggs { get; init; } = [];

    /// <summary>Unassigned allocations created from Administration → General.</summary>
    public IReadOnlyList<Allocation> FreeAllocations { get; init; } = [];
}

public sealed record AdminServerRow(Server Server, string OwnerName, string NodeName);

public sealed class AdminServerEditForm
{
    [Required, StringLength(191, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    [Required]
    public Guid OwnerId { get; set; }

    [Range(128, 1_048_576)]
    public int MemoryMb { get; set; }

    [Range(0, 1_048_576)]
    public int DiskMb { get; set; }

    [Range(0, 10_000)]
    public int CpuLimitPercent { get; set; }

    public bool Suspended { get; set; }

    public bool AccessRestricted { get; set; }
}

public sealed class AdminServerEditViewModel
{
    public Server Server { get; init; } = new();

    public IReadOnlyList<Node> Nodes { get; init; } = [];

    public IReadOnlyList<User> Users { get; init; } = [];
}
