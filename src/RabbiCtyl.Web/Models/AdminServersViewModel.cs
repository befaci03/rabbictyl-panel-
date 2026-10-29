using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Models;

public sealed class AdminServersViewModel
{
    public IReadOnlyList<AdminServerRow> Servers { get; init; } = [];

    public IReadOnlyList<Node> Nodes { get; init; } = [];

    public IReadOnlyList<User> Users { get; init; } = [];
}

public sealed record AdminServerRow(Server Server, string OwnerName, string NodeName);
