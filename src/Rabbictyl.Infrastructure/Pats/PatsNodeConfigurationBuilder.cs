using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Pats;

/// <summary>
/// Builds the config a node operator writes to pats' config.yml. The field
/// names mirror <c>PanelConfiguration</c> in pats.
/// </summary>
public static class PatsNodeConfigurationBuilder
{
    public static object Build(Node node, string panelUrl) =>
        new
        {
            debug = false,
            uuid = node.Uuid.ToString(),
            token_id = node.DaemonTokenId,
            token = node.DaemonToken,
            api = new
            {
                host = "0.0.0.0",
                port = node.DaemonPort,
                ssl = new
                {
                    enabled = !node.BehindProxy && node.UseSsl,
                    cert = $"/etc/letsencrypt/live/{node.Fqdn.ToLowerInvariant()}/fullchain.pem",
                    key = $"/etc/letsencrypt/live/{node.Fqdn.ToLowerInvariant()}/privkey.pem",
                },
                upload_limit = node.UploadSizeMb,
            },
            system = new
            {
                data = node.DaemonBase,
                sftp = new
                {
                    bind_port = node.DaemonSftpPort,
                },
            },
            allowed_mounts = Array.Empty<string>(),
            remote = panelUrl,
        };
}
