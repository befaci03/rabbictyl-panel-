using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;
using Rabbictyl.Infrastructure.Pats;
using Rabbictyl.Web.Models;
using Rabbictyl.Web.Security;

namespace Rabbictyl.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    private readonly IServerStore _servers;
    private readonly IUserStore _users;
    private readonly INodeStore _nodes;
    private readonly IPatsClientFactory _pats;

    public HomeController(
        IServerStore servers,
        IUserStore users,
        INodeStore nodes,
        IPatsClientFactory pats)
    {
        _servers = servers;
        _users = users;
        _nodes = nodes;
        _pats = pats;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        if (User.IsBootstrapAdmin())
        {
            return RedirectToAction("Setup", "Auth");
        }

        var showAllServers = User.IsInRole("admin")
            && string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<Server> servers;
        var ownerNames = new Dictionary<Guid, string>();

        if (showAllServers)
        {
            servers = await _servers.ListAsync(cancellationToken);
            ownerNames = (await _users.ListAsync(cancellationToken))
                .ToDictionary(user => user.Id, user => user.DisplayName);
        }
        else
        {
            var userId = User.GetUserId();
            servers = userId is null
                ? Array.Empty<Server>()
                : await _servers.ListForUserAsync(userId.Value, cancellationToken);
        }

        var model = new DashboardViewModel
        {
            Servers = servers,
            ShowAllServers = showAllServers,
            OwnerNames = ownerNames,
        };

        return View(model);
    }

    [HttpGet("/overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken = default)
    {
        if (User.IsBootstrapAdmin())
        {
            return RedirectToAction("Setup", "Auth");
        }

        var isAdmin = User.IsInRole("admin");
        IReadOnlyList<Server> servers;

        if (isAdmin)
        {
            servers = await _servers.ListAsync(cancellationToken);
        }
        else
        {
            var userId = User.GetUserId();
            servers = userId is null
                ? Array.Empty<Server>()
                : await _servers.ListForUserAsync(userId.Value, cancellationToken);
        }

        var nodes = (await _nodes.ListAsync(cancellationToken)).ToDictionary(node => node.Id);
        var owners = isAdmin
            ? (await _users.ListAsync(cancellationToken))
                .ToDictionary(user => user.Id, user => user.DisplayName)
            : new Dictionary<Guid, string>();

        var liveStates = await ReadLiveStatesAsync(servers, nodes, cancellationToken);

        var rows = servers
            .Select(server => new OverviewServerRow(
                server,
                nodes.GetValueOrDefault(server.NodeId)?.Name ?? "—",
                isAdmin ? owners.GetValueOrDefault(server.OwnerId, "Unknown user") : string.Empty,
                liveStates.TryGetValue(server.Uuid, out var live) ? live : string.Empty))
            .ToList();

        return View(new OverviewViewModel
        {
            Running = rows.Where(row => Bucket(row) == "running").ToList(),
            Busy = rows.Where(row => Bucket(row) == "busy").ToList(),
            Offline = rows.Where(row => Bucket(row) == "offline").ToList(),
            Failed = rows.Where(row => Bucket(row) == "failed").ToList(),
        });
    }

    [AllowAnonymous]
    [HttpGet("/error")]
    public IActionResult Error() => View();

    private async Task<Dictionary<Guid, string>> ReadLiveStatesAsync(
        IReadOnlyList<Server> servers,
        IReadOnlyDictionary<Guid, Node> nodes,
        CancellationToken cancellationToken)
    {
        var states = new ConcurrentDictionary<Guid, string>();

        // One daemon call per server, capped so an unreachable node cannot hold
        // the overview page hostage. Stored status is used as the fallback.
        await Task.WhenAll(servers.GroupBy(server => server.NodeId).Select(async group =>
        {
            if (!nodes.TryGetValue(group.Key, out var node))
            {
                return;
            }

            var client = _pats.Create(node);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));

            await Task.WhenAll(group.Select(async server =>
            {
                try
                {
                    var state = await client.GetServerAsync(server.Uuid, timeout.Token);

                    if (!string.IsNullOrWhiteSpace(state?.State))
                    {
                        states[server.Uuid] = state.State;
                    }
                }
                catch (Exception exception) when (
                    exception is PatsApiException or HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    // Fall back to the stored status below.
                }
            }));
        }));

        return new Dictionary<Guid, string>(states);
    }

    private static string Bucket(OverviewServerRow row)
    {
        if (row.Server.Status == ServerStatus.InstallFailed)
        {
            return "failed";
        }

        return row.LiveState.ToLowerInvariant() switch
        {
            "running" => "running",
            "starting" or "stopping" => "busy",
            "offline" => "offline",
            _ => row.Server.Status switch
            {
                ServerStatus.Running => "running",
                ServerStatus.Starting or ServerStatus.Stopping
                    or ServerStatus.Installing or ServerStatus.RestoringBackup => "busy",
                _ => "offline",
            },
        };
    }
}
