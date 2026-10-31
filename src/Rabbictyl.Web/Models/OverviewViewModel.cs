using Rabbictyl.Core.Entities;

namespace Rabbictyl.Web.Models;

public sealed record OverviewServerRow(
    Server Server,
    string NodeName,
    string OwnerName,
    string LiveState);

public sealed class OverviewViewModel
{
    public IReadOnlyList<OverviewServerRow> Running { get; init; } = [];

    public IReadOnlyList<OverviewServerRow> Busy { get; init; } = [];

    public IReadOnlyList<OverviewServerRow> Offline { get; init; } = [];

    public IReadOnlyList<OverviewServerRow> Failed { get; init; } = [];

    public int Total => Running.Count + Busy.Count + Offline.Count + Failed.Count;
}
