using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Infrastructure.Pats;
using RabbiCtyl.Web.Models;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

[Authorize]
[Route("server/{uuid}")]
public sealed class ServerController : Controller
{
    private static readonly string[] Sections =
        ["console", "files", "backups", "schedules", "users", "startup", "settings"];

    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;
    private readonly IUserStore _users;
    private readonly IPatsClientFactory _pats;
    private readonly ILogger<ServerController> _logger;

    public ServerController(
        IServerStore servers,
        INodeStore nodes,
        IUserStore users,
        IPatsClientFactory pats,
        ILogger<ServerController> logger)
    {
        _servers = servers;
        _nodes = nodes;
        _users = users;
        _pats = pats;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string uuid,
        string? section = "console",
        CancellationToken cancellationToken = default)
    {
        if (User.IsBootstrapAdmin())
        {
            return RedirectToAction("Setup", "Auth");
        }

        if (!Guid.TryParse(uuid, out var value))
        {
            return NotFound();
        }

        var server = await _servers.FindByUuidAsync(value, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        var userId = User.GetUserId();

        if (!User.IsInRole("admin") && (userId is null || server.OwnerId != userId.Value))
        {
            return Forbid();
        }

        var node = await _nodes.FindByIdAsync(server.NodeId, cancellationToken);
        var owner = await _users.FindByIdAsync(server.OwnerId, cancellationToken);
        var normalizedSection = Sections.Contains(section ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? section!.ToLowerInvariant()
            : "console";

        PatsServerState? state = null;
        string? logs = null;
        string? daemonError = null;

        if (node is not null)
        {
            try
            {
                var client = _pats.Create(node);
                state = await client.GetServerAsync(server.Uuid, cancellationToken);

                if (normalizedSection == "console")
                {
                    logs = FormatLogs(await client.GetLogsAsync(server.Uuid, 100, cancellationToken));
                }
            }
            catch (Exception exception) when (exception is PatsApiException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(exception, "Could not read pats state for server {Server}.", server.Uuid);
                daemonError = "The daemon is not reachable right now. Check the node link and try again.";
            }
        }

        ViewData["Title"] = server.Name;
        ViewData["Server"] = server;
        ViewData["ServerSection"] = normalizedSection;

        return View(new ServerPageViewModel
        {
            Server = server,
            Node = node,
            Section = normalizedSection,
            OwnerName = owner?.DisplayName ?? "Unknown user",
            OwnerUsername = owner?.Username ?? string.Empty,
            State = state,
            Logs = logs,
            DaemonError = daemonError,
            Environment = ParseEnvironment(server.EnvironmentJson),
        });
    }

    private static Dictionary<string, string> ParseEnvironment(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string FormatLogs(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.String)
            {
                return data.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // pats returns plain text for some builds; keep it as-is.
        }

        return raw;
    }
}
