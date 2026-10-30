using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;
using RabbiCtyl.Infrastructure.Extensions;
using RabbiCtyl.Infrastructure.Pats;
using RabbiCtyl.Web.Middleware;
using RabbiCtyl.Web.Models;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

[Authorize(Roles = "admin")]
[Route("admin")]
public sealed class AdminController : Controller
{
    private static readonly string[] AllowedLogoExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".avif", ".jxl"];

    private readonly IUserStore _users;
    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;
    private readonly IEggStore _eggs;
    private readonly IGameTemplateCatalog _templates;
    private readonly IAllocationStore _allocations;
    private readonly IApiKeyStore _apiKeys;
    private readonly IAddonCatalog _addons;
    private readonly IAddonState _addonState;
    private readonly IPanelBrandingStore _branding;
    private readonly IPatsClientFactory _pats;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AddonHost _addonHost;
    private readonly PanelOptions _options;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IUserStore users,
        IServerStore servers,
        INodeStore nodes,
        IEggStore eggs,
        IGameTemplateCatalog templates,
        IAllocationStore allocations,
        IApiKeyStore apiKeys,
        IAddonCatalog addons,
        IAddonState addonState,
        IPanelBrandingStore branding,
        IPatsClientFactory pats,
        IPasswordHasher passwordHasher,
        AddonHost addonHost,
        PanelOptions options,
        ILogger<AdminController> logger)
    {
        _users = users;
        _servers = servers;
        _nodes = nodes;
        _eggs = eggs;
        _templates = templates;
        _allocations = allocations;
        _apiKeys = apiKeys;
        _addons = addons;
        _addonState = addonState;
        _branding = branding;
        _pats = pats;
        _passwordHasher = passwordHasher;
        _addonHost = addonHost;
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
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var users = await _users.ListAsync(cancellationToken);

        return View(new AdminUsersViewModel
        {
            Users = users
                .Select(user => new AdminUserRow(user, _addonHost.ProtectionReason(user)))
                .ToList(),
        });
    }

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

        if (await _users.FindByLoginAsync(username, cancellationToken) is not null ||
            await _users.FindByLoginAsync(email, cancellationToken) is not null)
        {
            TempData["Error"] = "That username or email is already in use.";

            return RedirectToAction(nameof(Users));
        }

        var user = new User
        {
            Username = username,
            Email = email,
            NameFirst = form.FirstName.Trim(),
            NameLast = string.IsNullOrWhiteSpace(form.LastName) ? string.Empty : form.LastName.Trim(),
            PasswordHash = _passwordHasher.Hash(form.Password),
            IsRootAdmin = form.IsRootAdmin,
        };

        await _users.AddAsync(user, cancellationToken);

        TempData["Success"] = $"Created account {user.Username}.";

        return RedirectToAction(nameof(Users));
    }

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> UserEdit(Guid id, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (_addonHost.ProtectionReason(user) is { } reason)
        {
            TempData["Error"] = reason;

            return RedirectToAction(nameof(Users));
        }

        return View(new AdminUserEditViewModel { User = user });
    }

    [HttpPost("users/{id:guid}/update")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        AdminUserEditForm form,
        CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (_addonHost.ProtectionReason(user) is { } protection)
        {
            TempData["Error"] = protection;

            return RedirectToAction(nameof(Users));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the account details and try again.");

            return RedirectToAction(nameof(UserEdit), new { id });
        }

        var username = form.Username.Trim();
        var email = form.Email.Trim();

        var usernameOwner = await _users.FindByLoginAsync(username, cancellationToken);
        var emailOwner = await _users.FindByLoginAsync(email, cancellationToken);

        if (usernameOwner is not null && usernameOwner.Id != user.Id ||
            emailOwner is not null && emailOwner.Id != user.Id)
        {
            TempData["Error"] = "That username or email is already in use.";

            return RedirectToAction(nameof(UserEdit), new { id });
        }

        if (user.IsRootAdmin && !form.IsRootAdmin && User.GetUserId() == user.Id)
        {
            TempData["Error"] = "You cannot remove your own administrator access.";

            return RedirectToAction(nameof(UserEdit), new { id });
        }

        user.Username = username;
        user.Email = email;
        user.NameFirst = form.FirstName.Trim();
        user.NameLast = string.IsNullOrWhiteSpace(form.LastName) ? string.Empty : form.LastName.Trim();
        user.Language = string.IsNullOrWhiteSpace(form.Language) ? user.Language : form.Language.Trim();
        user.IsRootAdmin = form.IsRootAdmin;

        if (!string.IsNullOrWhiteSpace(form.NewPassword))
        {
            user.PasswordHash = _passwordHasher.Hash(form.NewPassword);
        }

        await _users.UpdateAsync(user, cancellationToken);

        TempData["Success"] = $"Updated account {user.Username}.";

        return RedirectToAction(nameof(Users));
    }

    [HttpPost("users/{id:guid}/delete")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (User.GetUserId() == user.Id)
        {
            TempData["Error"] = "You cannot delete your own account.";

            return RedirectToAction(nameof(Users));
        }

        if (_addonHost.ProtectionReason(user) is { } protection)
        {
            TempData["Error"] = protection;

            return RedirectToAction(nameof(Users));
        }

        var ownsServers = (await _servers.ListAsync(cancellationToken))
            .Any(server => server.OwnerId == user.Id);

        if (ownsServers)
        {
            TempData["Error"] =
                $"{user.Username} still owns servers. Transfer or delete those servers first.";

            return RedirectToAction(nameof(Users));
        }

        await _users.DeleteAsync(user, cancellationToken);

        TempData["Success"] = $"Deleted account {user.Username}.";

        return RedirectToAction(nameof(Users));
    }

    [HttpGet("general")]
    public async Task<IActionResult> General(CancellationToken cancellationToken)
    {
        var nodes = await _nodes.ListAsync(cancellationToken);
        var allocations = await _allocations.ListAsync(cancellationToken);
        var nodeNames = nodes.ToDictionary(node => node.Id, node => node.Name);

        return View(new AdminGeneralViewModel
        {
            Branding = _branding.Current,
            Nodes = nodes,
            Allocations = allocations
                .Select(allocation => new AdminAllocationRow(
                    allocation,
                    nodeNames.GetValueOrDefault(allocation.NodeId) ?? "Unknown node",
                    allocation.ServerId is not null))
                .ToList(),
        });
    }

    [HttpPost("general")]
    public async Task<IActionResult> General(
        GeneralSettingsForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the branding details and try again.");

            return RedirectToAction(nameof(General));
        }

        if (form.RemoveLogo)
        {
            await _branding.RemoveLogoAsync(cancellationToken);
        }

        if (form.Logo is { Length: > 0 } logo &&
            await StoreLogoAsync(logo, cancellationToken) is { } logoError)
        {
            TempData["Error"] = logoError;

            return RedirectToAction(nameof(General));
        }

        await _branding.SaveAsync(
            new PanelBranding
            {
                Name = string.IsNullOrWhiteSpace(form.Name) ? _options.App.Name : form.Name.Trim(),
                HideSidebarLogo = form.HideSidebarLogo,
                MaintenanceMode = form.MaintenanceMode,
                MaintenanceReason = string.IsNullOrWhiteSpace(form.MaintenanceReason)
                    ? null
                    : form.MaintenanceReason.Trim(),
            },
            cancellationToken);

        TempData["Success"] = form.MaintenanceMode
            ? "Saved. Maintenance mode is on for everyone except administrators."
            : "Panel branding updated.";

        return RedirectToAction(nameof(General));
    }

    [HttpPost("general/allocations")]
    public async Task<IActionResult> CreateAllocations(
        CreateAllocationsForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the allocation details and try again.");

            return RedirectToAction(nameof(General));
        }

        var node = await _nodes.FindByIdAsync(form.NodeId, cancellationToken);

        if (node is null)
        {
            TempData["Error"] = "The selected node does not exist.";

            return RedirectToAction(nameof(General));
        }

        var (ports, error) = ParsePorts(form.Ports);

        if (error is not null)
        {
            TempData["Error"] = error;

            return RedirectToAction(nameof(General));
        }

        var ip = form.Ip.Trim();
        var existing = await _allocations.ListForNodeAsync(node.Id, cancellationToken);
        var taken = existing
            .Where(allocation => string.Equals(allocation.Ip, ip, StringComparison.OrdinalIgnoreCase))
            .Select(allocation => allocation.Port)
            .ToHashSet();

        var additions = ports
            .Distinct()
            .Where(port => !taken.Contains(port))
            .Select(port => new Allocation { NodeId = node.Id, Ip = ip, Port = port })
            .ToList();

        if (additions.Count == 0)
        {
            TempData["Error"] = "Every requested port already exists on this node.";

            return RedirectToAction(nameof(General));
        }

        await _allocations.AddRangeAsync(additions, cancellationToken);

        TempData["Success"] = $"Added {additions.Count} allocation(s) to {node.Name}.";

        return RedirectToAction(nameof(General));
    }

    [HttpPost("general/allocations/{id:guid}/delete")]
    public async Task<IActionResult> DeleteAllocation(Guid id, CancellationToken cancellationToken)
    {
        var allocation = (await _allocations.ListAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == id);

        if (allocation is null)
        {
            return NotFound();
        }

        if (allocation.ServerId is not null)
        {
            TempData["Error"] = "That allocation is assigned to a server.";

            return RedirectToAction(nameof(General));
        }

        await _allocations.DeleteAsync(allocation, cancellationToken);

        TempData["Success"] = $"Removed allocation {allocation.Ip}:{allocation.Port}.";

        return RedirectToAction(nameof(General));
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
            Eggs = await _templates.ListAsync(cancellationToken),
            FreeAllocations = (await _allocations.ListAsync(cancellationToken))
                .Where(allocation => allocation.ServerId is null)
                .OrderBy(allocation => allocation.Port)
                .ToList(),
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

        Egg? egg = null;
        var fromNest = false;

        if (form.EggUuid is { } eggUuid && eggUuid != Guid.Empty)
        {
            var template = await _templates.FindAsync(eggUuid, cancellationToken);
            egg = template?.Egg;
            fromNest = template?.FromNest == true;

            if (egg is null)
            {
                TempData["Error"] = "The selected nest egg does not exist.";

                return RedirectToAction(nameof(Servers));
            }
        }
        else if (string.IsNullOrWhiteSpace(form.Startup) || string.IsNullOrWhiteSpace(form.Image))
        {
            TempData["Error"] = "Pick a nest egg or provide a startup command and container image.";

            return RedirectToAction(nameof(Servers));
        }

        var allocated = await ResolveAllocationAsync(node, form, cancellationToken);

        if (allocated.Error is not null)
        {
            TempData["Error"] = allocated.Error;

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
            AllocationIp = allocated.Allocation.Ip,
            AllocationPort = allocated.Allocation.Port,
            StartupCommand = form.Startup.Trim(),
            Image = form.Image.Trim(),
            AllocationMappingsJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, int[]> { [allocated.Allocation.Ip] = [allocated.Allocation.Port] }),
            Status = ServerStatus.Installing,
        };

        if (egg is not null)
        {
            // Nest templates live in TOML files and are re-read on every request, so
            // their in-memory id is not a database row. Keep one stored copy per nest
            // egg uuid; the server points at that row instead of a dangling id.
            if (fromNest)
            {
                egg = await _eggs.FindByUuidAsync(egg.Uuid, cancellationToken) ?? egg;

                if (await _eggs.FindByIdAsync(egg.Id, cancellationToken) is null)
                {
                    await _eggs.AddAsync(egg, cancellationToken);
                }
            }

            EggTemplate.ApplyTo(server, egg);
            server.StartupCommand = egg.StartupCommand;
        }

        server.DaemonConfigurationJson = PatsConfigurationBuilder.BuildSettings(server).GetRawText();

        await _servers.AddAsync(server, cancellationToken);

        allocated.Allocation.ServerId = server.Id;
        await _allocations.UpdateAsync(allocated.Allocation, cancellationToken);

        try
        {
            await _pats.Create(node).CreateServerAsync(server.Uuid, cancellationToken: cancellationToken);

            TempData["Success"] =
                $"Created {server.Name} on {node.Name} with allocation {allocated.Allocation.Ip}:{allocated.Allocation.Port}. pats is installing it now.";
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

    [HttpGet("games")]
    public async Task<IActionResult> Games(CancellationToken cancellationToken) =>
        View(await _templates.ListAsync(cancellationToken));

    [HttpPost("games/create")]
    public async Task<IActionResult> CreateGame(
        CreateGameTemplateForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the nest egg and try again.");

            return RedirectToAction(nameof(Games));
        }

        var egg = new Egg
        {
            Uuid = Guid.NewGuid(),
            Name = form.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
            Author = string.IsNullOrWhiteSpace(form.Author) ? string.Empty : form.Author.Trim(),
            DockerImage = form.Image.Trim(),
            DockerImagesJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, string> { ["default"] = form.Image.Trim() }),
            StartupCommand = form.Startup.Trim(),
            ScriptContainer = string.IsNullOrWhiteSpace(form.ScriptContainer) ? "alpine:3.20" : form.ScriptContainer.Trim(),
            ScriptEntrypoint = string.IsNullOrWhiteSpace(form.ScriptEntrypoint) ? "ash" : form.ScriptEntrypoint.Trim(),
            ScriptInstall = string.IsNullOrWhiteSpace(form.InstallScript) ? null : form.InstallScript.Trim(),
            ProcessConfigurationJson = EggTemplate.BuildProcessConfiguration(
                SplitLines(form.DonePatterns),
                form.StopType,
                form.StopValue),
            VariablesJson = EggTemplate.BuildVariables(SplitLines(form.Variables)),
        };

        await _eggs.AddAsync(egg, cancellationToken);

        TempData["Success"] = $"Added nest egg {egg.Name}.";

        return RedirectToAction(nameof(Games));
    }

    [HttpPost("games/{uuid}/delete")]
    public async Task<IActionResult> DeleteGame(string uuid, CancellationToken cancellationToken)
    {
        var egg = Guid.TryParse(uuid, out var value)
            ? await _eggs.FindByUuidAsync(value, cancellationToken)
            : null;

        if (egg is null)
        {
            return NotFound();
        }

        var usedByServer = (await _servers.ListAsync(cancellationToken))
            .Any(server => server.EggId == egg.Id);

        if (usedByServer)
        {
            TempData["Error"] = $"{egg.Name} is still used by a server. Delete or recreate those servers first.";

            return RedirectToAction(nameof(Games));
        }

        await _eggs.DeleteAsync(egg, cancellationToken);

        TempData["Success"] = $"Deleted nest egg {egg.Name}.";

        return RedirectToAction(nameof(Games));
    }

    [HttpPost("games/{uuid}/update")]
    public async Task<IActionResult> UpdateGame(
        string uuid,
        UpdateGameTemplateForm form,
        CancellationToken cancellationToken)
    {
        var template = Guid.TryParse(uuid, out var value)
            ? await _templates.FindAsync(value, cancellationToken)
            : null;

        if (template is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the nest egg and try again.");

            return RedirectToAction(nameof(Games));
        }

        // Editing a nest template stores a panel copy that shadows the TOML egg.
        // Converting panel changes back to TOML is lossy, so the file is never
        // rewritten.
        var egg = await _eggs.FindByUuidAsync(template.Egg.Uuid, cancellationToken);
        var created = egg is null;

        egg ??= EggTemplate.CopyOf(template.Egg);

        egg.Name = form.Name.Trim();
        egg.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        egg.Author = string.IsNullOrWhiteSpace(form.Author) ? string.Empty : form.Author.Trim();
        egg.StartupCommand = form.Startup.Trim();
        egg.DockerImage = form.Image.Trim();
        egg.DockerImagesJson = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { ["default"] = form.Image.Trim() });

        if (created)
        {
            await _eggs.AddAsync(egg, cancellationToken);
        }
        else
        {
            await _eggs.UpdateAsync(egg, cancellationToken);
        }

        TempData["Success"] = created && template.FromNest
            ? $"Saved {egg.Name} as a panel copy. The nest file is unchanged."
            : $"Updated nest egg {egg.Name}.";

        return RedirectToAction(nameof(Games));
    }

    [HttpGet("rapi")]
    public async Task<IActionResult> Rapi(CancellationToken cancellationToken) =>
        View(new AdminRapiViewModel
        {
            Keys = await _apiKeys.ListAsync(cancellationToken),
            Nodes = await _nodes.ListAsync(cancellationToken),
        });

    [HttpPost("rapi/create")]
    public async Task<IActionResult> CreateApiKey(
        CreateApiKeyForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the API key details and try again.");

            return RedirectToAction(nameof(Rapi));
        }

        var identifier = RandomToken(8);
        var secret = RandomToken(24);
        var userId = User.GetUserId() ?? Guid.Empty;

        await _apiKeys.AddAsync(
            new ApiKey
            {
                UserId = userId,
                Identifier = identifier,
                TokenHash = ApplicationApiKeyMiddleware.Hash(secret),
                Memo = string.IsNullOrWhiteSpace(form.Memo) ? null : form.Memo.Trim(),
                ExpiresAt = form.ExpiresInDays > 0
                    ? DateTimeOffset.UtcNow.AddDays(form.ExpiresInDays)
                    : null,
            },
            cancellationToken);

        TempData["NewKey"] = $"rbtl_{identifier}.{secret}";
        TempData["Success"] = "API key created. Copy it now: it is not shown again.";

        return RedirectToAction(nameof(Rapi));
    }

    [HttpPost("rapi/{id:guid}/delete")]
    public async Task<IActionResult> DeleteApiKey(Guid id, CancellationToken cancellationToken)
    {
        var key = (await _apiKeys.ListAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == id);

        if (key is null)
        {
            return NotFound();
        }

        await _apiKeys.DeleteAsync(key, cancellationToken);

        TempData["Success"] = "API key revoked.";

        return RedirectToAction(nameof(Rapi));
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

    private async Task<string?> StoreLogoAsync(IFormFile logo, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(logo.FileName).ToLowerInvariant();

        if (!AllowedLogoExtensions.Contains(extension))
        {
            return "The logo must be a PNG, JPEG, WebP, GIF, SVG, AVIF or JPEG XL image.";
        }

        if (logo.Length > 2 * 1024 * 1024)
        {
            return "The logo must be 2 MB or smaller.";
        }

        // The image is stored exactly as uploaded; the panel serves its own content
        // type for every accepted format, SVG included.
        await using var stream = logo.OpenReadStream();
        await _branding.SaveLogoAsync(stream, extension, cancellationToken);

        return null;
    }

    private async Task<AllocationResult> ResolveAllocationAsync(
        Node node,
        CreateServerForm form,
        CancellationToken cancellationToken)
    {
        if (form.AllocationId is { } allocationId && allocationId != Guid.Empty)
        {
            var chosen = (await _allocations.ListAsync(cancellationToken))
                .FirstOrDefault(allocation => allocation.Id == allocationId);

            if (chosen is null || chosen.NodeId != node.Id)
            {
                return AllocationResult.Failed("Pick an allocation that belongs to this node.");
            }

            return chosen.ServerId is not null
                ? AllocationResult.Failed($"{chosen.Ip}:{chosen.Port} is already assigned to a server.")
                : AllocationResult.Success(chosen);
        }

        var ip = form.AllocationIp.Trim();
        var allocation = await _allocations.FindAsync(node.Id, ip, form.AllocationPort, cancellationToken);

        if (allocation is { ServerId: not null })
        {
            return AllocationResult.Failed($"{ip}:{form.AllocationPort} is already assigned to a server.");
        }

        if (allocation is null)
        {
            allocation = new Allocation { NodeId = node.Id, Ip = ip, Port = form.AllocationPort };
            await _allocations.AddRangeAsync([allocation], cancellationToken);
        }

        return AllocationResult.Success(allocation);
    }

    private static (List<int> Ports, string? Error) ParsePorts(string input)
    {
        var ports = new List<int>();

        foreach (var token in input.Split(
                     [',', ';', ' ', '\n', '\r', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = token.IndexOf('-');

            if (separator > 0)
            {
                if (!int.TryParse(token[..separator], out var start) ||
                    !int.TryParse(token[(separator + 1)..], out var end))
                {
                    return ([], $"'{token}' is not a port or a range.");
                }

                if (start < 1 || end > 65535 || start > end)
                {
                    return ([], $"'{token}' is outside 1-65535.");
                }

                if (end - start > 1000)
                {
                    return ([], $"'{token}' covers more than 1001 ports.");
                }

                for (var port = start; port <= end; port++)
                {
                    ports.Add(port);
                }
            }
            else if (int.TryParse(token, out var port) && port is >= 1 and <= 65535)
            {
                ports.Add(port);
            }
            else
            {
                return ([], $"'{token}' is not a valid port.");
            }

            if (ports.Count > 2000)
            {
                return ([], "Add at most 2000 ports at once.");
            }
        }

        return ports.Count == 0
            ? ([], "Enter at least one port or range.")
            : (ports, null);
    }

    [HttpGet("servers/{uuid}")]
    public async Task<IActionResult> ServerEdit(string uuid, CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        return server is null
            ? NotFound()
            : View(new AdminServerEditViewModel
            {
                Server = server,
                Nodes = await _nodes.ListAsync(cancellationToken),
                Users = await _users.ListAsync(cancellationToken),
            });
    }

    [HttpPost("servers/{uuid}/update")]
    public async Task<IActionResult> UpdateServer(
        string uuid,
        AdminServerEditForm form,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the server details and try again.");

            return RedirectToAction(nameof(ServerEdit), new { uuid });
        }

        if (form.OwnerId != server.OwnerId &&
            await _users.FindByIdAsync(form.OwnerId, cancellationToken) is null)
        {
            TempData["Error"] = "The selected owner does not exist.";

            return RedirectToAction(nameof(ServerEdit), new { uuid });
        }

        server.Name = form.Name.Trim();
        server.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        server.OwnerId = form.OwnerId;
        server.MemoryLimitMb = form.MemoryMb;
        server.DiskLimitMb = form.DiskMb;
        server.CpuLimitPercent = form.CpuLimitPercent;
        server.Suspended = form.Suspended;
        server.AccessRestricted = form.AccessRestricted;

        if (form.Suspended)
        {
            server.Status = ServerStatus.Suspended;
        }
        else if (server.Status == ServerStatus.Suspended)
        {
            server.Status = ServerStatus.Offline;
        }

        server.DaemonConfigurationJson = PatsConfigurationBuilder.BuildSettings(server).GetRawText();
        await _servers.UpdateAsync(server, cancellationToken);

        TempData["Success"] = $"Updated {server.Name}.";

        return RedirectToAction(nameof(Servers));
    }

    private async Task<Server?> FindServerAsync(string uuid, CancellationToken cancellationToken) =>
        Guid.TryParse(uuid, out var value) ? await _servers.FindByUuidAsync(value, cancellationToken) : null;

    private async Task<Node?> FindNodeAsync(string uuid, CancellationToken cancellationToken) =>
        Guid.TryParse(uuid, out var value) ? await _nodes.FindByUuidAsync(value, cancellationToken) : null;

    private static IEnumerable<string> SplitLines(string? value) =>
        (value ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string FirstError(string fallback) =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => error.ErrorMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
        ?? fallback;

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private sealed record AllocationResult(Allocation Allocation, string? Error)
    {
        public static AllocationResult Success(Allocation allocation) => new(allocation, null);

        public static AllocationResult Failed(string error) => new(new Allocation(), error);
    }
}
