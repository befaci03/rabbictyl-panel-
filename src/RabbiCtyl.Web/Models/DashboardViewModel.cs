using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Models;

public sealed class DashboardViewModel
{
    public int ServerCount { get; init; }

    public int NodeCount { get; init; }

    public int UserCount { get; init; }

    public IReadOnlyList<Server> Servers { get; init; } = [];
}
