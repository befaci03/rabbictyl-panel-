using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Infrastructure.Pats;
using RabbiCtyl.Web.Models;

namespace RabbiCtyl.Web.Controllers;

[Authorize(Roles = "admin")]
[Route("admin")]
public sealed class AdminController : Controller
{
    private readonly IUserStore _users;
    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;
    private readonly IAddonCatalog _addons;
    private readonly IAddonState _addonState;
    private readonly IPatsClientFactory _pats;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PanelOptions _options;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IUserStore users,
        IServerStore servers,
        INodeStore nodes,
        IAddonCatalog addons,
        IAddonState addonState,
        IPatsClientFactory pats,
        IPasswordHasher passwordHasher,
        PanelOptions options,
        ILogger<AdminController> logger)
    {
        _users = users;
        _servers = servers;
        _nodes = nodes;
        _addons = addons;
        _addonState = addonState;
        _pats = pats;
        _passwordHasher = passwordHasher;
        _options = options;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Nodes));

    [HttpGet("nodes")]
    public async Task<IActionResult> Nodes(CancellationToken cancellationToken) =>
        View(new AdminNodesViewModel
        {
            Nodes = await _nodes.ListAsync(cancellationToken),
            PanelUrl = _options.App.Url.TrimEnd('/'),
        });

    [HttpPost("nodes/create")]
    public async Task<IActionResult> CreateNode(
        CreateNodeForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the node details and try again.");

            return RedirectToAction(nameof(Nodes));
        }

        var node = new Node
        {
            Name = form.Name.Trim(),
            Fqdn = form.Fqdn.Trim(),
            UseSsl = form.UseSsl,
            DaemonPort = form.DaemonPort,
            DaemonSftpPort = form.DaemonSftpPort,
            BehindProxy = form.BehindProxy,
            MemoryLimitMb = form.MemoryMb,
            MemoryOverallocatePercent = form.MemoryOverallocatePercent,
            DiskLimitMb = form.DiskMb,
            DiskOverallocatePercent = form.DiskOverallocatePercent,
            UploadSizeMb = form.UploadSizeMb,
            DaemonBase = form.DaemonBase.Trim(),
            DaemonTokenId = RandomToken(8),
            DaemonToken = RandomToken(32),
        };

        await _nodes.AddAsync(node, cancellationToken);

        TempData["Success"] =
            $"Created node {node.Name}. Copy its config.yml below into pats, start pats, then use Link.";

        return RedirectToAction(nameof(Nodes));
    }

    [HttpPost("nodes/{uuid}/link")]
    public async Task<IActionResult> LinkNode(string uuid, CancellationToken cancellationToken)
    {
        var node = await FindNodeAsync(uuid, cancellationToken);

        if (node is null)
        {
            return NotFound();
        }

        try
        {
            var system = await _pats.Create(node).GetSystemAsync(cancellationToken);

            TempData["Success"] =
                $"Linked {node.Name}: pats {system.Version} on {system.Os} ({system.Architecture}).";
        }
        catch (Exception exception) when (exception is PatsApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Could not link node {Node}.", node.Name);
            TempData["Error"] =
                $"Could not reach pats for {node.Name}. Verify the config, token, FQDN and that pats is running.";
        }

        return RedirectToAction(nameof(Nodes));
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        View(await _users.ListAsync(cancellationToken));

    [HttpPost("users/create")]
    public async Task<IActionResult> CreateUser(
        CreateUserForm form,
        CancellationToken cancellationToken)
    {
        var username = form.Username.Trim();
        var email = form.Email.Trim();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the account details and try again.");

            return RedirectToAction(nameof(Users));
        }

        if (await _users.FindByLoginAsync(username, cancellationToken) is not null)
        {
            TempData["Error"] = "That username or email is already in use.";

            return RedirectToAction(nameof(Users));
        }

        if (await _users.FindByLoginAsync(email, cancellationToken) is not null)
        {
            TempData["Error"] = "That username or email is already in use.";

            return RedirectToAction(nameof(Users));
        }

        var user = new User
        {
            Username = username,
            Email = email,
            NameFirst = form.FirstName.Trim(),
            NameLast = form.LastName.Trim(),
            PasswordHash = _passwordHasher.Hash(form.Password),
            IsRootAdmin = form.IsRootAdmin,
        };

        await _users.AddAsync(user, cancellationToken);

        TempData["Success"] = $"Created account {user.Username}.";

        return RedirectToAction(nameof(Users));
    }

    [HttpGet("servers")]
    public async Task<IActionResult> Servers(CancellationToken cancellationToken)
    {
        var servers = await _servers.ListAsync(cancellationToken);
        var users = (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id);
        var nodes = (await _nodes.ListAsync(cancellationToken)).ToDictionary(node => node.Id);

        var model = new AdminServersViewModel
        {
            Nodes = nodes.Values.OrderBy(node => node.Name).ToList(),
            Users = users.Values.OrderBy(user => user.DisplayName).ToList(),
            Servers = servers
                .Select(server => new AdminServerRow(
                    server,
                    users.GetValueOrDefault(server.OwnerId)?.DisplayName ?? "Unknown user",
                    nodes.GetValueOrDefault(server.NodeId)?.Name ?? "Unknown node"))
                .ToList(),
        };

        return View(model);
    }

    [HttpPost("servers/create")]
    public async Task<IActionResult> CreateServer(
        CreateServerForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the server details and try again.");

            return RedirectToAction(nameof(Servers));
        }

        var node = await _nodes.FindByUuidAsync(form.NodeUuid, cancellationToken);

        if (node is null)
        {
            TempData["Error"] = "The selected node does not exist.";

            return RedirectToAction(nameof(Servers));
        }

        if (await _users.FindByIdAsync(form.OwnerId, cancellationToken) is null)
        {
            TempData["Error"] = "The selected owner does not exist.";

            return RedirectToAction(nameof(Servers));
        }

        var server = new Server
        {
            Uuid = Guid.NewGuid(),
            Name = form.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
            OwnerId = form.OwnerId,
            NodeId = node.Id,
            MemoryLimitMb = form.MemoryMb,
            DiskLimitMb = form.DiskMb,
            CpuLimitPercent = form.CpuLimitPercent,
            AllocationIp = form.AllocationIp.Trim(),
            AllocationPort = form.AllocationPort,
            StartupCommand = form.Startup.Trim(),
            Image = form.Image.Trim(),
            AllocationMappingsJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, int[]> { [form.AllocationIp.Trim()] = [form.AllocationPort] }),
            Status = ServerStatus.Installing,
        };

        server.DaemonConfigurationJson = PatsConfigurationBuilder.BuildSettings(server).GetRawText();

        await _servers.AddAsync(server, cancellationToken);

        try
        {
            await _pats.Create(node).CreateServerAsync(server.Uuid, cancellationToken: cancellationToken);

            TempData["Success"] =
                $"Created {server.Name} on {node.Name}. pats is installing it now.";
        }
        catch (Exception exception) when (exception is PatsApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(exception, "pats rejected server {Server}.", server.Uuid);

            server.Status = ServerStatus.InstallFailed;
            await _servers.UpdateAsync(server, cancellationToken);

            TempData["Error"] =
                $"The panel saved {server.Name}, but pats could not start the install. Link the node and try again.";
        }

        return RedirectToAction(nameof(Servers));
    }

    [HttpGet("addons")]
    public async Task<IActionResult> Addons(CancellationToken cancellationToken) =>
        View(await _addons.ListAsync(cancellationToken: cancellationToken));

    [HttpPost("addons/{id}/toggle")]
    public async Task<IActionResult> ToggleAddon(
        string id,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var addon = await _addons.FindAsync(id, cancellationToken);

        if (addon is null)
        {
            return NotFound();
        }

        await _addonState.SetEnabledAsync(addon.Id, enabled, cancellationToken);

        TempData["Success"] = enabled
            ? $"Enabled {addon.Name}. Refresh the panel to load its browser scripts."
            : $"Disabled {addon.Name}.";

        if (addon.AssemblyPath is not null)
        {
            TempData["Notice"] =
                "C# addons finish starting or stopping after the panel restarts.";
        }

        return RedirectToAction(nameof(Addons));
    }

    private async Task<Node?> FindNodeAsync(string uuid, CancellationToken cancellationToken) =>
        Guid.TryParse(uuid, out var value) ? await _nodes.FindByUuidAsync(value, cancellationToken) : null;

    private string FirstError(string fallback) =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
        ?? fallback;

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
