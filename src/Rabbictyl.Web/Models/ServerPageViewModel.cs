using Rabbictyl.Core.Entities;
using Rabbictyl.Infrastructure.Pats;

namespace Rabbictyl.Web.Models;

public sealed class ServerPageViewModel
{
    public Server Server { get; init; } = new();

    public Node? Node { get; init; }

    public string Section { get; init; } = "console";

    public string OwnerName { get; init; } = "Unknown user";

    public string OwnerUsername { get; init; } = string.Empty;

    public PatsServerState? State { get; init; }

    public string? Logs { get; init; }

    public string? DaemonError { get; init; }

    /// <summary>True when an administrator restricted access for this viewer.</summary>
    public bool AccessRestricted { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
