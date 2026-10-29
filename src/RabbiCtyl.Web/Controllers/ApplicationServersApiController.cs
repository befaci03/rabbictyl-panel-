using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Infrastructure.Pats;

namespace RabbiCtyl.Web.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/application/servers")]
public sealed class ApplicationServersApiController : ControllerBase
{
    private readonly IServerStore _servers;
    private readonly INodeStore _nodes;
    private readonly IUserStore _users;
    private readonly IPatsClientFactory _pats;
    private readonly ILogger<ApplicationServersApiController> _logger;

    public ApplicationServersApiController(
        IServerStore servers,
        INodeStore nodes,
        IUserStore users,
        IPatsClientFactory pats,
        ILogger<ApplicationServersApiController> logger)
    {
        _servers = servers;
        _nodes = nodes;
        _users = users;
        _pats = pats;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(new { data = (await _servers.ListAsync(cancellationToken)).Select(ToDto) });

    [HttpGet("{uuid}")]
    public async Task<IActionResult> Get(string uuid, CancellationToken cancellationToken)
    {
        var server = await FindAsync(uuid, cancellationToken);

        return server is null ? NotFound() : Ok(ToDto(server));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateServerRequest request,
        CancellationToken cancellationToken)
    {
        var node = await _nodes.FindByUuidAsync(request.NodeUuid, cancellationToken);

        if (node is null)
        {
            return ValidationProblem("The selected node does not exist.");
        }

        if (await _users.FindByIdAsync(request.OwnerId, cancellationToken) is null)
        {
            return ValidationProblem("The selected owner does not exist.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.AllocationPort is < 1 or > 65535)
        {
            return ValidationProblem("A name and a valid allocation port are required.");
        }

        var server = new Server
        {
            Uuid = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            OwnerId = request.OwnerId,
            NodeId = node.Id,
            EggUuid = request.EggUuid ?? string.Empty,
            MemoryLimitMb = request.MemoryMb,
            SwapMb = request.SwapMb,
            DiskLimitMb = request.DiskMb,
            IoWeight = request.IoWeight,
            CpuLimitPercent = request.CpuLimitPercent,
            Threads = request.Threads,
            OomDisabled = request.OomDisabled,
            AllocationIp = request.AllocationIp,
            AllocationPort = request.AllocationPort,
            AllocationMappingsJson = request.AllocationMappingsJson,
            MountsJson = request.MountsJson,
            StartupCommand = request.Startup,
            Image = request.Image,
            EnvironmentJson = request.EnvironmentJson,
            ProcessConfigurationJson = request.ProcessConfigurationJson,
            InstallScriptJson = request.InstallScriptJson,
            EggFileDenylistJson = request.EggFileDenylistJson,
            EggForceOutgoingIp = request.EggForceOutgoingIp,
            SkipScripts = request.SkipScripts,
            AllocationLimit = request.AllocationLimit,
            DatabaseLimit = request.DatabaseLimit,
            BackupLimit = request.BackupLimit,
            Status = ServerStatus.Installing,
        };

        server.DaemonConfigurationJson = PatsConfigurationBuilder.BuildSettings(server).GetRawText();

        await _servers.AddAsync(server, cancellationToken);

        try
        {
            await _pats.Create(node).CreateServerAsync(server.Uuid, cancellationToken: cancellationToken);
        }
        catch (PatsApiException exception)
        {
            _logger.LogError(exception, "pats rejected server {Server}.", server.Uuid);
            server.Status = ServerStatus.InstallFailed;
            await _servers.UpdateAsync(server, cancellationToken);

            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }

        return CreatedAtAction(nameof(Get), new { uuid = server.Uuid }, ToDto(server));
    }

    [HttpPatch("{uuid}")]
    public async Task<IActionResult> Update(
        string uuid,
        [FromBody] UpdateServerRequest request,
        CancellationToken cancellationToken)
    {
        var server = await FindAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        if (request.Name is not null)
        {
            server.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            server.Description = request.Description;
        }

        if (request.MemoryMb is not null)
        {
            server.MemoryLimitMb = request.MemoryMb.Value;
        }

        if (request.SwapMb is not null)
        {
            server.SwapMb = request.SwapMb.Value;
        }

        if (request.DiskMb is not null)
        {
            server.DiskLimitMb = request.DiskMb.Value;
        }

        if (request.IoWeight is not null)
        {
            server.IoWeight = request.IoWeight.Value;
        }

        if (request.CpuLimitPercent is not null)
        {
            server.CpuLimitPercent = request.CpuLimitPercent.Value;
        }

        if (request.Threads is not null)
        {
            server.Threads = request.Threads;
        }

        if (request.Startup is not null)
        {
            server.StartupCommand = request.Startup;
        }

        if (request.Image is not null)
        {
            server.Image = request.Image;
        }

        server.DaemonConfigurationJson = null;
        await _servers.UpdateAsync(server, cancellationToken);

        var node = await _nodes.FindByIdAsync(server.NodeId, cancellationToken);

        if (node is not null)
        {
            try
            {
                await _pats.Create(node).SyncAsync(server.Uuid, cancellationToken);
            }
            catch (PatsApiException exception)
            {
                _logger.LogWarning(exception, "Could not sync server {Server} with pats.", server.Uuid);
            }
        }

        return Ok(ToDto(server));
    }

    [HttpDelete("{uuid}")]
    public async Task<IActionResult> Delete(string uuid, CancellationToken cancellationToken)
    {
        var server = await FindAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        var node = await _nodes.FindByIdAsync(server.NodeId, cancellationToken);

        if (node is not null)
        {
            try
            {
                await _pats.Create(node).DeleteServerAsync(server.Uuid, cancellationToken);
            }
            catch (PatsApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already gone from the daemon; remove the panel record anyway.
            }
        }

        await _servers.DeleteAsync(server, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/suspend")]
    public async Task<IActionResult> Suspend(string uuid, CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        server.Suspended = true;
        server.Status = ServerStatus.Suspended;
        await _servers.UpdateAsync(server, cancellationToken);

        try
        {
            await _pats.Create(node).PowerAsync(server.Uuid, "stop", 30, cancellationToken);
        }
        catch (PatsApiException exception)
        {
            _logger.LogWarning(exception, "Could not stop server {Server} while suspending.", server.Uuid);
        }

        return NoContent();
    }

    [HttpPost("{uuid}/unsuspend")]
    public async Task<IActionResult> Unsuspend(string uuid, CancellationToken cancellationToken)
    {
        var server = await FindAsync(uuid, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        server.Suspended = false;
        server.Status = ServerStatus.Offline;
        await _servers.UpdateAsync(server, cancellationToken);

        return NoContent();
    }

    [HttpPost("{uuid}/reinstall")]
    public async Task<IActionResult> Reinstall(string uuid, CancellationToken cancellationToken)
    {
        var (server, node) = await ResolveAsync(uuid, cancellationToken);

        if (server is null || node is null)
        {
            return NotFound();
        }

        server.Status = ServerStatus.Installing;
        await _servers.UpdateAsync(server, cancellationToken);
        await _pats.Create(node).ReinstallAsync(server.Uuid, cancellationToken);

        return Accepted();
    }

    private async Task<Server?> FindAsync(string uuid, CancellationToken cancellationToken) =>
        Guid.TryParse(uuid, out var value) ? await _servers.FindByUuidAsync(value, cancellationToken) : null;

    private async Task<(Server? Server, Node? Node)> ResolveAsync(
        string uuid,
        CancellationToken cancellationToken)
    {
        var server = await FindAsync(uuid, cancellationToken);
        var node = server is null ? null : await _nodes.FindByIdAsync(server.NodeId, cancellationToken);

        return (server, node);
    }

    private static object ToDto(Server server) => new
    {
        uuid = server.Uuid,
        name = server.Name,
        description = server.Description,
        status = server.Status.ToString().ToLowerInvariant(),
        suspended = server.Suspended,
        node_id = server.NodeId,
        owner_id = server.OwnerId,
        memory_mb = server.MemoryLimitMb,
        swap_mb = server.SwapMb,
        disk_mb = server.DiskLimitMb,
        cpu_percent = server.CpuLimitPercent,
        allocation = new { ip = server.AllocationIp, port = server.AllocationPort },
        startup = server.StartupCommand,
        image = server.Image,
    };

    public sealed class CreateServerRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid OwnerId { get; set; }
        public Guid NodeUuid { get; set; }
        public string? EggUuid { get; set; }
        public int MemoryMb { get; set; } = 512;
        public int SwapMb { get; set; }
        public int DiskMb { get; set; } = 1024;
        public int IoWeight { get; set; } = 500;
        public int CpuLimitPercent { get; set; } = 100;
        public string? Threads { get; set; }
        public bool OomDisabled { get; set; } = true;
        public string AllocationIp { get; set; } = "0.0.0.0";
        public int AllocationPort { get; set; }
        public string AllocationMappingsJson { get; set; } = "{}";
        public string MountsJson { get; set; } = "[]";
        public string Startup { get; set; } = string.Empty;
        public string Image { get; set; } = "alpine:3.20";
        public bool SkipScripts { get; set; }
        public string EnvironmentJson { get; set; } = "{}";
        public string ProcessConfigurationJson { get; set; } = "{}";
        public string InstallScriptJson { get; set; } = "{}";
        public string EggFileDenylistJson { get; set; } = "[]";
        public bool EggForceOutgoingIp { get; set; }
        public int AllocationLimit { get; set; }
        public int DatabaseLimit { get; set; }
        public int BackupLimit { get; set; }
    }

    public sealed class UpdateServerRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? MemoryMb { get; set; }
        public int? SwapMb { get; set; }
        public int? DiskMb { get; set; }
        public int? IoWeight { get; set; }
        public int? CpuLimitPercent { get; set; }
        public string? Threads { get; set; }
        public string? Startup { get; set; }
        public string? Image { get; set; }
    }
}
