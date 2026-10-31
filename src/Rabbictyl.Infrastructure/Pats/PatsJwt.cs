using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Pats;

/// <summary>
/// Creates the short-lived HS256 tokens pats verifies for websockets, file
/// transfers, backups and server transfers. Pats uses the daemon token as the
/// HMAC key, so no extra secret is required in the panel.
/// </summary>
public static class PatsJwt
{
    public const string WebsocketScope = "websocket";
    public const string FileUploadScope = "file-upload";
    public const string FileDownloadScope = "file-download";
    public const string BackupDownloadScope = "backup-download";
    public const string TransferScope = "transfer";

    private static readonly HashSet<string> OwnerPermissions =
    [
        "websocket.connect",
        "control.console",
        "control.start",
        "control.stop",
        "control.restart",
        "backup.read",
        "backup.create",
        "backup.restore",
        "backup.delete",
        "file.read",
        "file.create",
        "file.update",
        "file.delete",
        "file.archive",
        "file.sftp",
        "allocation.read",
        "allocation.create",
        "allocation.update",
        "allocation.delete",
        "startup.read",
        "startup.update",
        "settings.rename",
        "settings.reinstall",
        "activity.read",
        "user.read",
        "user.create",
        "user.update",
        "user.delete",
        "database.read",
        "database.create",
        "database.update",
        "database.delete",
        "schedule.read",
        "schedule.create",
        "schedule.update",
        "schedule.delete",
        "task.read",
        "task.create",
        "task.update",
        "task.delete",
        "admin.websocket.errors",
        "admin.websocket.install",
        "admin.websocket.transfer",
    ];

    public static string CreateWebsocketToken(
        Node node,
        Guid userId,
        Guid serverUuid,
        IEnumerable<string>? permissions = null,
        TimeSpan? lifetime = null)
    {
        var jti = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{userId}{serverUuid}")))
            .ToLowerInvariant();

        return Create(
            node,
            userId.ToString(),
            "websocket",
            WebsocketScope,
            lifetime ?? TimeSpan.FromMinutes(10),
            new Dictionary<string, object?>
            {
                ["user_uuid"] = userId.ToString(),
                ["server_uuid"] = serverUuid.ToString(),
                ["permissions"] = (permissions ?? OwnerPermissions).ToArray(),
            },
            jti);
    }

    public static string CreateFileToken(
        Node node,
        Guid userId,
        Guid serverUuid,
        string scope,
        string filePath,
        string uniqueId,
        TimeSpan? lifetime = null) =>
        Create(
            node,
            userId.ToString(),
            scope,
            scope,
            lifetime ?? TimeSpan.FromMinutes(15),
            new Dictionary<string, object?>
            {
                ["user_uuid"] = userId.ToString(),
                ["server_uuid"] = serverUuid.ToString(),
                ["file_path"] = filePath,
                ["unique_id"] = uniqueId,
            });

    public static string CreateBackupToken(
        Node node,
        Guid userId,
        Guid serverUuid,
        Guid backupUuid,
        string uniqueId,
        TimeSpan? lifetime = null) =>
        Create(
            node,
            userId.ToString(),
            BackupDownloadScope,
            BackupDownloadScope,
            lifetime ?? TimeSpan.FromMinutes(15),
            new Dictionary<string, object?>
            {
                ["user_uuid"] = userId.ToString(),
                ["server_uuid"] = serverUuid.ToString(),
                ["backup_uuid"] = backupUuid.ToString(),
                ["unique_id"] = uniqueId,
            });

    public static string CreateTransferToken(
        Node node,
        Guid serverUuid,
        TimeSpan? lifetime = null) =>
        Create(
            node,
            serverUuid.ToString(),
            TransferScope,
            TransferScope,
            lifetime ?? TimeSpan.FromMinutes(15),
            new Dictionary<string, object?>
            {
                ["server_uuid"] = serverUuid.ToString(),
            });

    public static IReadOnlyList<string> PermissionsForOwner() => OwnerPermissions.ToArray();

    public static string Create(
        Node node,
        string subject,
        string audience,
        string scope,
        TimeSpan lifetime,
        IReadOnlyDictionary<string, object?>? claims = null,
        string? jti = null)
    {
        var now = DateTimeOffset.UtcNow;

        var payload = new Dictionary<string, object?>
        {
            ["iss"] = "rabbictyl",
            ["sub"] = subject,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime).ToUnixTimeSeconds(),
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
            ["scope"] = scope,
        };

        if (claims is not null)
        {
            foreach (var (key, value) in claims)
            {
                payload[key] = value;
            }
        }

        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT",
        }));

        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{header}.{body}";
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(node.DaemonToken),
            Encoding.ASCII.GetBytes(signingInput));

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
