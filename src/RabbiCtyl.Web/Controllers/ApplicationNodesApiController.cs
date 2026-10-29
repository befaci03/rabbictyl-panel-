using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Infrastructure.Pats;

namespace RabbiCtyl.Web.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/application/nodes")]
public sealed class ApplicationNodesApiController : ControllerBase
{
    private readonly INodeStore _nodes;
    private readonly PanelOptions _options;

    public ApplicationNodesApiController(INodeStore nodes, PanelOptions options)
    {
        _nodes = nodes;
        _options = options;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(new { data = (await _nodes.ListAsync(cancellationToken)).Select(ToDto) });

    [HttpGet("{uuid}")]
    public async Task<IActionResult> Get(string uuid, CancellationToken cancellationToken)
    {
        var node = await FindAsync(uuid, cancellationToken);

        return node is null ? NotFound() : Ok(ToDto(node));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateNodeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Fqdn))
        {
            return ValidationProblem("A node name and FQDN are required.");
        }

        var node = new Node
        {
            Name = request.Name.Trim(),
            Fqdn = request.Fqdn.Trim(),
            UseSsl = request.UseSsl,
            DaemonPort = request.DaemonPort,
            DaemonSftpPort = request.DaemonSftpPort,
            BehindProxy = request.BehindProxy,
            MemoryLimitMb = request.MemoryMb,
            MemoryOverallocatePercent = request.MemoryOverallocatePercent,
            DiskLimitMb = request.DiskMb,
            DiskOverallocatePercent = request.DiskOverallocatePercent,
            UploadSizeMb = request.UploadSizeMb,
            DaemonBase = request.DaemonBase,
            DaemonTokenId = RandomToken(8),
            DaemonToken = RandomToken(32),
        };

        await _nodes.AddAsync(node, cancellationToken);

        return CreatedAtAction(nameof(Get), new { uuid = node.Uuid }, ToDto(node));
    }

    [HttpPatch("{uuid}")]
    public async Task<IActionResult> Update(
        string uuid,
        [FromBody] UpdateNodeRequest request,
        CancellationToken cancellationToken)
    {
        var node = await FindAsync(uuid, cancellationToken);

        if (node is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            node.Name = request.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Fqdn))
        {
            node.Fqdn = request.Fqdn.Trim();
        }

        if (request.UseSsl is not null)
        {
            node.UseSsl = request.UseSsl.Value;
        }

        if (request.DaemonPort is not null)
        {
            node.DaemonPort = request.DaemonPort.Value;
        }

        if (request.DaemonSftpPort is not null)
        {
            node.DaemonSftpPort = request.DaemonSftpPort.Value;
        }

        if (request.MaintenanceMode is not null)
        {
            node.MaintenanceMode = request.MaintenanceMode.Value;
        }

        await _nodes.UpdateAsync(node, cancellationToken);

        return Ok(ToDto(node));
    }

    [HttpDelete("{uuid}")]
    public async Task<IActionResult> Delete(string uuid, CancellationToken cancellationToken)
    {
        var node = await FindAsync(uuid, cancellationToken);

        if (node is null)
        {
            return NotFound();
        }

        await _nodes.DeleteAsync(node, cancellationToken);

        return NoContent();
    }

    [HttpGet("{uuid}/configuration")]
    public async Task<IActionResult> Configuration(string uuid, CancellationToken cancellationToken)
    {
        var node = await FindAsync(uuid, cancellationToken);

        return node is null
            ? NotFound()
            : Ok(PatsNodeConfigurationBuilder.Build(node, _options.App.Url));
    }

    private async Task<Node?> FindAsync(string uuid, CancellationToken cancellationToken) =>
        Guid.TryParse(uuid, out var value) ? await _nodes.FindByUuidAsync(value, cancellationToken) : null;

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private static object ToDto(Node node) => new
    {
        uuid = node.Uuid,
        name = node.Name,
        fqdn = node.Fqdn,
        scheme = node.Scheme,
        daemon_port = node.DaemonPort,
        daemon_sftp_port = node.DaemonSftpPort,
        behind_proxy = node.BehindProxy,
        maintenance_mode = node.MaintenanceMode,
        memory_mb = node.MemoryLimitMb,
        disk_mb = node.DiskLimitMb,
        token_id = node.DaemonTokenId,
    };

    public sealed class CreateNodeRequest
    {
        public string Name { get; set; } = string.Empty;

        public string Fqdn { get; set; } = string.Empty;

        public bool UseSsl { get; set; } = true;
        public int DaemonPort { get; set; } = 8080;
        public int DaemonSftpPort { get; set; } = 2022;
        public bool BehindProxy { get; set; }
        public int MemoryMb { get; set; } = 1024;
        public int MemoryOverallocatePercent { get; set; }
        public int DiskMb { get; set; } = 10240;
        public int DiskOverallocatePercent { get; set; }
        public int UploadSizeMb { get; set; } = 100;
        public string DaemonBase { get; set; } = "/var/lib/pterodactyl/volumes";
    }

    public sealed class UpdateNodeRequest
    {
        public string? Name { get; set; }
        public string? Fqdn { get; set; }
        public bool? UseSsl { get; set; }
        public int? DaemonPort { get; set; }
        public int? DaemonSftpPort { get; set; }
        public bool? MaintenanceMode { get; set; }
    }
}
