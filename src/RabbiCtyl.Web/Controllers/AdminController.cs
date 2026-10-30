using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;
using RabbiCtyl.Infrastructure.Pats;
using RabbiCtyl.Web.Models;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

[Authorize(Roles = "admin")]
[Route("admin")]
public sealed class AdminController : Controller
{
    private readonly IUserStore _users;
    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;
    private readonly IEggStore _eggs;
    private readonly IGameTemplateCatalog _templates;
    private readonly IOAuthProviderStore _oauth;
    private readonly IAddonCatalog _addons;
    private readonly IAddonState _addonState;
    private readonly IPanelBrandingStore _branding;
    private readonly IPatsClientFactory _pats;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PanelOptions _options;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IUserStore users,
        IServerStore servers,
        INodeStore nodes,
        IEggStore eggs,
        IGameTemplateCatalog templates,
        IOAuthProviderStore oauth,
        IAddonCatalog addons,
        IAddonState addonState,
        IPanelBrandingStore branding,
        IPatsClientFactory pats,
        IPasswordHasher passwordHasher,
        PanelOptions options,
        ILogger<AdminController> logger)
    {
        _users = users;
        _servers = servers;
        _nodes = nodes;
        _eggs = eggs;
        _templates = templates;
        _oauth = oauth;
        _addons = addons;
        _addonState = addonState;
        _branding = branding;
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

        return user is null
            ? NotFound()
            : View(new AdminUserEditViewModel { User = user });
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

    [HttpGet("general")]
    public IActionResult General() => View(_branding.Current);

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

        if (form.Logo is { Length: > 0 } logo)
        {
            var extension = Path.GetExtension(logo.FileName).ToLowerInvariant();

            if (!AllowedLogoExtensions.Contains(extension))
            {
                TempData["Error"] = "The logo must be a PNG, JPEG, WebP, GIF or SVG image.";

                return RedirectToAction(nameof(General));
            }

            if (logo.Length > 2 * 1024 * 1024)
            {
                TempData["Error"] = "The logo must be 2 MB or smaller.";

                return RedirectToAction(nameof(General));
            }

            await using var stream = logo.OpenReadStream();
            await _branding.SaveLogoAsync(stream, extension, cancellationToken);
        }

        await _branding.SaveAsync(
            new PanelBranding
            {
                Name = string.IsNullOrWhiteSpace(form.Name) ? _options.App.Name : form.Name.Trim(),
                HideSidebarLogo = form.HideSidebarLogo,
            },
            cancellationToken);

        TempData["Success"] = "Panel branding updated.";

        return RedirectToAction(nameof(General));
    }

    private static readonly string[] AllowedLogoExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg"];

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
                TempData["Error"] = "The selected game template does not exist.";

                return RedirectToAction(nameof(Servers));
            }
        }
        else if (string.IsNullOrWhiteSpace(form.Startup) || string.IsNullOrWhiteSpace(form.Image))
        {
            TempData["Error"] = "Pick a game template or provide a startup command and container image.";

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
            TempData["Error"] = FirstError("Check the game template and try again.");

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

        TempData["Success"] = $"Added game template {egg.Name}.";

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

        TempData["Success"] = $"Deleted game template {egg.Name}.";

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
            TempData["Error"] = FirstError("Check the game template and try again.");

            return RedirectToAction(nameof(Games));
        }

        // Editing a nest template stores a panel copy that shadows the TOML egg.
        // Converting panel changes back into TOML is lossy, so the file is never
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
            : $"Updated game template {egg.Name}.";

        return RedirectToAction(nameof(Games));
    }

    [HttpGet("oauth2")]
    public async Task<IActionResult> OAuth2(CancellationToken cancellationToken)
    {
        var enabledAddons = await _addons.ListAsync(enabledOnly: true, cancellationToken: cancellationToken);

        return View(new OAuthAdminViewModel
        {
            Providers = await _oauth.ListAsync(cancellationToken),
            AddonEnabled = enabledAddons.Any(addon =>
                string.Equals(addon.Id, "oauth2", StringComparison.OrdinalIgnoreCase)),
        });
    }

    [HttpPost("oauth2/create")]
    public async Task<IActionResult> CreateOAuthProvider(
        OAuthProviderForm form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the OAuth2 provider details and try again.");

            return RedirectToAction(nameof(OAuth2));
        }

        var id = form.Id.Trim().ToLowerInvariant();

        if (await _oauth.FindAsync(id, cancellationToken) is not null)
        {
            TempData["Error"] = $"A provider with the id '{id}' already exists.";

            return RedirectToAction(nameof(OAuth2));
        }

        await _oauth.SaveAsync(
            new OAuthProvider
            {
                Id = id,
                Name = form.Name.Trim(),
                ClientId = form.ClientId.Trim(),
                ClientSecret = form.ClientSecret.Trim(),
                AuthorizeUrl = form.AuthorizeUrl.Trim(),
                TokenUrl = form.TokenUrl.Trim(),
                UserInfoUrl = form.UserInfoUrl.Trim(),
                Scopes = form.Scopes.Trim(),
                UserIdField = form.UserIdField.Trim(),
                EmailField = form.EmailField.Trim(),
                UsernameField = form.UsernameField.Trim(),
                DisplayNameField = form.DisplayNameField.Trim(),
                Enabled = form.Enabled,
            },
            cancellationToken);

        TempData["Success"] = $"Added the {form.Name} sign-in provider.";

        return RedirectToAction(nameof(OAuth2));
    }

    [HttpPost("oauth2/{id}/update")]
    public async Task<IActionResult> UpdateOAuthProvider(
        string id,
        OAuthProviderEditForm form,
        CancellationToken cancellationToken)
    {
        var provider = await _oauth.FindAsync(id, cancellationToken);

        if (provider is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError("Check the OAuth2 provider details and try again.");

            return RedirectToAction(nameof(OAuth2));
        }

        provider.Name = form.Name.Trim();
        provider.ClientId = form.ClientId.Trim();

        if (!string.IsNullOrWhiteSpace(form.ClientSecret))
        {
            provider.ClientSecret = form.ClientSecret.Trim();
        }

        provider.AuthorizeUrl = form.AuthorizeUrl.Trim();
        provider.TokenUrl = form.TokenUrl.Trim();
        provider.UserInfoUrl = form.UserInfoUrl.Trim();
        provider.Scopes = form.Scopes.Trim();
        provider.UserIdField = form.UserIdField.Trim();
        provider.EmailField = form.EmailField.Trim();
        provider.UsernameField = form.UsernameField.Trim();
        provider.DisplayNameField = form.DisplayNameField.Trim();
        provider.Enabled = form.Enabled;

        await _oauth.SaveAsync(provider, cancellationToken);

        TempData["Success"] = $"Updated the {provider.Name} sign-in provider.";

        return RedirectToAction(nameof(OAuth2));
    }

    [HttpPost("oauth2/{id}/delete")]
    public async Task<IActionResult> DeleteOAuthProvider(string id, CancellationToken cancellationToken)
    {
        var provider = await _oauth.FindAsync(id, cancellationToken);

        if (provider is null)
        {
            return NotFound();
        }

        await _oauth.DeleteAsync(provider.Id, cancellationToken);

        TempData["Success"] = $"Removed the {provider.Name} sign-in provider.";

        return RedirectToAction(nameof(OAuth2));
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
}
