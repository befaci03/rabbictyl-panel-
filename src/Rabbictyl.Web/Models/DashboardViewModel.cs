using Rabbictyl.Core.Entities;

namespace Rabbictyl.Web.Models;

public sealed class DashboardViewModel
{
    public IReadOnlyList<Server> Servers { get; init; } = [];

    public bool ShowAllServers { get; init; }

    public IReadOnlyDictionary<Guid, string> OwnerNames { get; init; } =
        new Dictionary<Guid, string>();
}
