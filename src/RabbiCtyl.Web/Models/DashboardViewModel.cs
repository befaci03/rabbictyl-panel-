using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Models;

public sealed class DashboardViewModel
{
    public IReadOnlyList<Server> Servers { get; init; } = [];

    public bool ShowAllServers { get; init; }

    public IReadOnlyDictionary<Guid, string> OwnerNames { get; init; } =
        new Dictionary<Guid, string>();
}
