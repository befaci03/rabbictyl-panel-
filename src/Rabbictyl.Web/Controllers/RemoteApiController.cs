using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;
using Rabbictyl.Infrastructure.Pats;
using Rabbictyl.Web.Middleware;

namespace Rabbictyl.Web.Controllers;

/// <summary>
/// Endpoints pats calls back into. These are authenticated by
/// <see cref="DaemonAuthenticationMiddleware"/>, not by cookies.
/// </summary>
[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/remote")]
public sealed class RemoteApiController : ControllerBase
{
    private readonly IServerStore _servers;
    private readonly IUserStore _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<RemoteApiController> _logger;

    public RemoteApiController(
        IServerStore servers,
        IUserStore users,
        IPasswordHasher passwordHasher,
        ILogger<RemoteApiController> logger)
    {
        _servers = servers;
        _users = users;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    private Node Node =>
        HttpContext.Items[DaemonAuthenticationMiddleware.NodeItemKey] as Node
        ?? throw new InvalidOperationException("Daemon authentication did not run.");

    [HttpGet("servers")]
    public async Task<IActionResult> Servers(
        [FromQuery] int page = 1,
        [FromQuery(Name = "per_page")] int perPage = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        perPage = Math.Clamp(perPage, 1, 100);

        var total = await _servers.CountAsync(cancellationToken);
        var servers = await _servers.ListPagedAsync(page, perPage, cancellationToken);

        var data = servers
            .Where(server => server.NodeId == Node.Id)
            .Select(server =>
            {
                var configuration = PatsConfigurationBuilder.BuildConfiguration(server);

                return new
                {
                    uuid = server.Uuid.ToString(),
                    settings = configuration.Settings,
                    process_configuration = configuration.ProcessConfiguration,
                };
            })
            .ToArray();

        return Ok(new
        {
            data,
            meta = new
            {
                current_page = page,
                from = data.Length == 0 ? 0 : ((page - 1) * perPage) + 1,
                last_page = Math.Max(1, (int)Math.Ceiling(total / (double)perPage)),
                per_page = perPage,
                to = data.Length == 0 ? 0 : ((page - 1) * perPage) + data.Length,
                total,
            },
        });
    }

    [HttpGet("servers/{uuid}")]
    public async Task<IActionResult> Server(string uuid, CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        return server is null
            ? NotFound(new { error = "The requested resource does not exist on this instance." })
            : Ok(PatsConfigurationBuilder.BuildConfiguration(server));
    }

    [HttpGet("servers/{uuid}/install")]
    public async Task<IActionResult> InstallScript(string uuid, CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        return server is null
            ? NotFound(new { error = "The requested resource does not exist on this instance." })
            : Ok(PatsConfigurationBuilder.BuildInstallScript(server));
    }

    [HttpPost("servers/{uuid}/install")]
    public async Task<IActionResult> InstallStatus(
        string uuid,
        [FromBody] InstallStatusRequest request,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound(new { error = "The requested resource does not exist on this instance." });
        }

        server.Status = request.Successful ? ServerStatus.Offline : ServerStatus.InstallFailed;
        server.InstalledAt = request.Successful ? DateTimeOffset.UtcNow : server.InstalledAt;
        await _servers.UpdateAsync(server, cancellationToken);

        return NoContent();
    }

    [HttpPost("servers/reset")]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        var servers = await _servers.ListAsync(cancellationToken);

        foreach (var server in servers.Where(server => server.NodeId == Node.Id && server.Status == ServerStatus.Installing))
        {
            server.Status = ServerStatus.Offline;
            await _servers.UpdateAsync(server, cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("servers/{uuid}/archive")]
    public async Task<IActionResult> Archive(
        string uuid,
        [FromBody] ArchiveStatusRequest request,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound(new { error = "The requested resource does not exist on this instance." });
        }

        if (!request.Successful)
        {
            _logger.LogWarning("pats failed to archive server {Server}.", server.Uuid);
        }

        server.Status = ServerStatus.Offline;
        await _servers.UpdateAsync(server, cancellationToken);

        return NoContent();
    }

    [HttpPost("servers/{uuid}/transfer/{state}")]
    public async Task<IActionResult> TransferStatus(
        string uuid,
        string state,
        CancellationToken cancellationToken)
    {
        var server = await FindServerAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound(new { error = "The requested resource does not exist on this instance." });
        }

        if (!string.Equals(state, "success", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("pats reported a failed transfer for server {Server}.", server.Uuid);
        }

        server.Status = ServerStatus.Offline;
        await _servers.UpdateAsync(server, cancellationToken);

        return NoContent();
    }

    [HttpPost("sftp/auth")]
    public async Task<IActionResult> SftpAuth(
        [FromBody] SftpAuthRequest request,
        CancellationToken cancellationToken)
    {
        var login = request.Username.Split('.', 2)[0];
        var user = await _users.FindByLoginAsync(login, cancellationToken);

        var authorized = user is not null
            && (string.Equals(request.Type, "public_key", StringComparison.OrdinalIgnoreCase)
                ? Rabbictyl.Core.Extensions.SshKeys.Matches(user, request.Password)
                : _passwordHasher.Verify(request.Password, user.PasswordHash));

        if (user is null || !authorized)
        {
            return Unauthorized(new { error = "The credentials provided were invalid." });
        }

        var servers = await _servers.ListForUserAsync(user.Id, cancellationToken);

        if (request.Username.Contains('.'))
        {
            var suffix = request.Username.Split('.', 2)[1];
            servers = servers
                .Where(server => ShortIdentifier(server).Equals(suffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Suspended servers drop SFTP sessions exactly like Pterodactyl does, and
        // only servers that live on the calling node may authenticate.
        var server = servers.FirstOrDefault(server => server.NodeId == Node.Id && !server.Suspended);

        return server is null
            ? Unauthorized(new { error = "The credentials provided were invalid." })
            : Ok(new
            {
                server = server.Uuid.ToString(),
                user = user.Id.ToString(),
                permissions = PatsJwt.PermissionsForOwner(),
            });
    }

    [HttpGet("backups/{uuid}")]
    public IActionResult Backup(string uuid) =>
        NotFound(new { error = "Remote backup uploads are not configured for this node." });

    [HttpPost("backups/{uuid}")]
    public IActionResult BackupStatus(string uuid) => NoContent();

    [HttpPost("backups/{uuid}/restore")]
    public IActionResult BackupRestoreStatus(string uuid) => NoContent();

    [HttpPost("activity")]
    public IActionResult Activity([FromBody] System.Text.Json.JsonElement activity) => NoContent();

    private static string ShortIdentifier(Server server) =>
        server.Uuid.ToString("N")[..8];

    private async Task<Server?> FindServerAsync(string uuid, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(uuid, out var value))
        {
            return null;
        }

        var server = await _servers.FindByUuidAsync(value, cancellationToken);

        return server is not null && server.NodeId == Node.Id ? server : null;
    }

    public sealed class InstallStatusRequest
    {
        [JsonPropertyName("successful")]
        public bool Successful { get; set; }

        [JsonPropertyName("reinstall")]
        public bool Reinstall { get; set; }
    }

    public sealed class ArchiveStatusRequest
    {
        [JsonPropertyName("successful")]
        public bool Successful { get; set; }
    }

    public sealed class SftpAuthRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "password";

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("ip")]
        public string Ip { get; set; } = string.Empty;
    }
}
