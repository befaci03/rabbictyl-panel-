using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;
using Rabbictyl.Infrastructure.Pats;
using Rabbictyl.Web.Security;

namespace Rabbictyl.Web.Controllers;

/// <summary>
/// Client-facing control API. Closely follows Pterodactyl's
/// <c>/api/client/servers/{server}</c> routes and proxies daemon work to pats.
/// </summary>
[ApiController]
[Authorize]
[Route("api/client/servers")]
public sealed class ClientServersApiController : ControllerBase
{
    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;
    private readonly IPatsClientFactory _pats;
    private readonly ILogger<ClientServersApiController> _logger;

    public ClientServersApiController(
        IServerStore servers,
        INodeStore nodes,
        IPatsClientFactory pats,
        ILogger<ClientServersApiController> logger)
    {
        _servers = servers;
        _nodes = nodes;
        _pats = pats;
        _logger = logger;
    }

    private bool IsAdmin => User.IsInRole("admin");

    /// <summary>
    /// Restricted servers keep running, but their owner may only use files,
    /// console and server settings until an administrator lifts the restriction.
    /// </summary>
    private bool IsRestricted(Server server) => server.AccessRestricted && !IsAdmin;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var servers = IsAdmin || userId is null
            ? await _servers.ListAsync(cancellationToken)
            : await _servers.ListForUserAsync(userId.Value, cancellationToken);

        return Ok(new { data = servers.Select(ToDto).ToArray() });
    }

    [HttpGet("{uuid}")]
    public async Task<IActionResult> Get(string uuid, CancellationToken cancellationToken)
    {
        var (server, _) = await ResolveAsync(uuid, cancellationToken);

        return server is null ? NotFound() : Ok(ToDto(server));
    }

    [HttpGet("{uuid}/resources")]
    public async Task<IActionResult> Resources(string uuid, CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var state = await _pats.Create(node).GetServerAsync(server.Uuid, cancellationToken);

        return state is null ? NotFound() : Ok(state);
    }

    [HttpGet("{uuid}/websocket")]
    public async Task<IActionResult> Websocket(string uuid, CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var token = PatsJwt.CreateWebsocketToken(
            node,
            userId,
            server.Uuid,
            PatsJwt.PermissionsForOwner());

        var prefix = node.UseSsl ? "wss" : "ws";

        return Ok(new
        {
            token,
            socket = $"{prefix}://{node.Fqdn}:{node.DaemonPort}/api/servers/{server.Uuid}/ws",
        });
    }

    [HttpPost("{uuid}/power")]
    public async Task<IActionResult> Power(
        string uuid,
        [FromBody] PowerRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        if (request.Action is not ("start" or "stop" or "restart" or "kill"))
        {
            return ValidationProblem("The power action must be start, stop, restart or kill.");
        }

        if (server.Suspended && request.Action is "start" or "restart")
        {
            return BadRequest(new { error = "Cannot start or restart a server that is suspended." });
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        await _pats.Create(node).PowerAsync(server.Uuid, request.Action, cancellationToken: cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/command")]
    public async Task<IActionResult> Command(
        string uuid,
        [FromBody] CommandRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return ValidationProblem("A command is required.");
        }

        await _pats.Create(node).SendCommandsAsync(server.Uuid, [request.Command], cancellationToken);

        return NoContent();
    }

    [HttpGet("{uuid}/files/list")]
    public async Task<IActionResult> ListFiles(
        string uuid,
        [FromQuery] string directory = "/",
        CancellationToken cancellationToken = default)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var json = await _pats.Create(node).ListDirectoryAsync(server.Uuid, directory, cancellationToken);

        return Content(json, "application/json");
    }

    [HttpGet("{uuid}/files/contents")]
    public async Task<IActionResult> FileContents(
        string uuid,
        [FromQuery] string file,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var contents = await _pats.Create(node).ReadFileAsync(server.Uuid, file, cancellationToken: cancellationToken);

        return Content(contents, "text/plain; charset=utf-8");
    }

    [HttpPost("{uuid}/files/write")]
    public async Task<IActionResult> WriteFile(
        string uuid,
        [FromQuery] string file,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        var contents = await reader.ReadToEndAsync(cancellationToken);

        await _pats.Create(node).WriteFileAsync(server.Uuid, file, contents, cancellationToken);

        return NoContent();
    }

    [HttpGet("{uuid}/files/download")]
    public async Task<IActionResult> DownloadFile(
        string uuid,
        [FromQuery] string file,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var token = PatsJwt.CreateFileToken(
            node,
            User.GetUserId() ?? Guid.Empty,
            server.Uuid,
            PatsJwt.FileDownloadScope,
            file,
            Guid.NewGuid().ToString("N"));

        return Ok(new { url = $"{node.DaemonBaseUrl}/download/file?token={Uri.EscapeDataString(token)}" });
    }

    [HttpGet("{uuid}/files/upload")]
    public async Task<IActionResult> UploadFile(
        string uuid,
        [FromQuery] string directory = "/",
        CancellationToken cancellationToken = default)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var token = PatsJwt.CreateFileToken(
            node,
            User.GetUserId() ?? Guid.Empty,
            server.Uuid,
            PatsJwt.FileUploadScope,
            string.Empty,
            Guid.NewGuid().ToString("N"));

        return Ok(new
        {
            url = $"{node.DaemonBaseUrl}/upload/file?token={Uri.EscapeDataString(token)}&directory={Uri.EscapeDataString(directory)}",
        });
    }

    [HttpPut("{uuid}/files/rename")]
    public async Task<IActionResult> RenameFiles(
        string uuid,
        [FromBody] RenameFilesRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        await _pats.Create(node).RenameAsync(
            server.Uuid,
            request.Root,
            request.Files.Select(file => (file.From, file.To)).ToArray(),
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/files/copy")]
    public async Task<IActionResult> CopyFile(
        string uuid,
        [FromBody] LocationRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        await _pats.Create(node).CopyAsync(server.Uuid, request.Location, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/files/delete")]
    public async Task<IActionResult> DeleteFiles(
        string uuid,
        [FromBody] DeleteFilesRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        await _pats.Create(node).DeleteFilesAsync(server.Uuid, request.Root, request.Files, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/files/create-folder")]
    public async Task<IActionResult> CreateFolder(
        string uuid,
        [FromBody] CreateFolderRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        await _pats.Create(node).CreateDirectoryAsync(server.Uuid, request.Name, request.Path, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/files/compress")]
    public async Task<IActionResult> CompressFiles(
        string uuid,
        [FromBody] FilesRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        var json = await _pats.Create(node).CompressAsync(server.Uuid, request.Root, request.Files, cancellationToken);

        return Content(json, "application/json");
    }

    [HttpPost("{uuid}/files/decompress")]
    public async Task<IActionResult> DecompressFile(
        string uuid,
        [FromBody] DecompressRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        await _pats.Create(node).DecompressAsync(server.Uuid, request.Root, request.File, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/files/chmod")]
    public async Task<IActionResult> ChmodFiles(
        string uuid,
        [FromBody] ChmodRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        await _pats.Create(node).ChmodAsync(
            server.Uuid,
            request.Root,
            request.Files.Select(file => (file.File, file.Mode)).ToArray(),
            cancellationToken);

        return NoContent();
    }

    [HttpGet("{uuid}/backups")]
    public IActionResult Backups(string uuid) => Ok(new { data = Array.Empty<object>() });

    [HttpPost("{uuid}/backups")]
    public async Task<IActionResult> CreateBackup(string uuid, CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        var backupUuid = Guid.NewGuid();
        await _pats.Create(node).CreateBackupAsync(server.Uuid, backupUuid, cancellationToken: cancellationToken);

        return Accepted(new { uuid = backupUuid, status = "creating" });
    }

    [HttpPost("{uuid}/backups/{backupUuid}/restore")]
    public async Task<IActionResult> RestoreBackup(
        string uuid,
        string backupUuid,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null || !Guid.TryParse(backupUuid, out var backup))
        {
            return NotFound();
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        await _pats.Create(node).RestoreBackupAsync(server.Uuid, backup, cancellationToken: cancellationToken);

        return Accepted();
    }

    [HttpDelete("{uuid}/backups/{backupUuid}")]
    public async Task<IActionResult> DeleteBackup(
        string uuid,
        string backupUuid,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null || !Guid.TryParse(backupUuid, out var backup))
        {
            return NotFound();
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        await _pats.Create(node).DeleteBackupAsync(server.Uuid, backup, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/settings/rename")]
    public async Task<IActionResult> Rename(
        string uuid,
        [FromBody] RenameServerRequest request,
        CancellationToken cancellationToken)
    {
        var (server, _) = await ResolveAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("A name is required.");
        }

        server.Name = request.Name.Trim();
        await _servers.UpdateAsync(server, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/settings/reinstall")]
    public async Task<IActionResult> Reinstall(string uuid, CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        server.Status = ServerStatus.Installing;
        await _servers.UpdateAsync(server, cancellationToken);
        await _pats.Create(node).ReinstallAsync(server.Uuid, cancellationToken);

        return Accepted();
    }

    [HttpGet("{uuid}/startup")]
    public async Task<IActionResult> Startup(string uuid, CancellationToken cancellationToken)
    {
        var (server, _) = await ResolveAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        return Ok(new
        {
            startup = server.StartupCommand,
            image = server.Image,
            environment = ParseEnvironment(server.EnvironmentJson),
        });
    }

    [HttpPut("{uuid}/startup/variable")]
    public async Task<IActionResult> UpdateVariable(
        string uuid,
        [FromBody] VariableRequest request,
        CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        if (IsRestricted(server))
        {
            return BadRequest(new { error = "Access to this server is temporarily restricted." });
        }

        var environment = ParseEnvironment(server.EnvironmentJson);
        environment[request.Key] = request.Value;
        server.EnvironmentJson = JsonSerializer.Serialize(environment);
        server.DaemonConfigurationJson = null;
        await _servers.UpdateAsync(server, cancellationToken);

        try
        {
            await _pats.Create(node).SyncAsync(server.Uuid, cancellationToken);
        }
        catch (PatsApiException exception)
        {
            _logger.LogWarning(exception, "Could not sync startup variables for server {Server}.", server.Uuid);
        }

        return NoContent();
    }

    [HttpGet("{uuid}/network/allocations")]
    public async Task<IActionResult> Allocations(string uuid, CancellationToken cancellationToken)
    {
        var (server, _) = await ResolveAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            data = new[]
            {
                new { ip = server.AllocationIp, port = server.AllocationPort, primary = true },
            },
            mappings = ParseMappings(server.AllocationMappingsJson),
        });
    }

    [HttpGet("{uuid}/schedules")]
    public IActionResult Schedules(string uuid) => Ok(new { data = Array.Empty<object>() });

    [HttpGet("{uuid}/databases")]
    public IActionResult Databases(string uuid) => Ok(new { data = Array.Empty<object>() });

    [HttpGet("{uuid}/users")]
    public IActionResult Users(string uuid) => Ok(new { data = Array.Empty<object>() });

    private async Task<(Server? Server, Node? Node)> ResolveAsync(
        string uuid,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(uuid, out var value))
        {
            return (null, null);
        }

        var server = await _servers.FindByUuidAsync(value, cancellationToken);

        if (server is null)
        {
            return (null, null);
        }

        var userId = User.GetUserId();

        if (!IsAdmin && (userId is null || server.OwnerId != userId.Value))
        {
            return (null, null);
        }

        var node = await _nodes.FindByIdAsync(server.NodeId, cancellationToken);

        return (server, node);
    }

    private static object ToDto(Server server) => new
    {
        uuid = server.Uuid,
        name = server.Name,
        description = server.Description,
        status = server.Status.ToString().ToLowerInvariant(),
        suspended = server.Suspended,
        memory_mb = server.MemoryLimitMb,
        disk_mb = server.DiskLimitMb,
        cpu_percent = server.CpuLimitPercent,
        allocation = new { ip = server.AllocationIp, port = server.AllocationPort },
    };

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

    private static JsonElement ParseMappings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, object>());
        }
    }

    public sealed class PowerRequest
    {
        public string Action { get; set; } = string.Empty;
    }

    public sealed class CommandRequest
    {
        public string Command { get; set; } = string.Empty;
    }

    public sealed class RenameFilesRequest
    {
        public string Root { get; set; } = "/";
        public List<FileMove> Files { get; set; } = [];
    }

    public sealed class FileMove
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    public sealed class LocationRequest
    {
        public string Location { get; set; } = string.Empty;
    }

    public sealed class DeleteFilesRequest
    {
        public string Root { get; set; } = "/";
        public List<string> Files { get; set; } = [];
    }

    public sealed class CreateFolderRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
    }

    public sealed class FilesRequest
    {
        public string Root { get; set; } = "/";
        public List<string> Files { get; set; } = [];
    }

    public sealed class DecompressRequest
    {
        public string Root { get; set; } = "/";
        public string File { get; set; } = string.Empty;
    }

    public sealed class ChmodRequest
    {
        public string Root { get; set; } = "/";
        public List<ChmodFile> Files { get; set; } = [];
    }

    public sealed class ChmodFile
    {
        public string File { get; set; } = string.Empty;
        public string Mode { get; set; } = "644";
    }

    public sealed class RenameServerRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class VariableRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
