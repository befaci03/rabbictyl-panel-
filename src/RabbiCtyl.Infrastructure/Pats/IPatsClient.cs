using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Pats;

public interface IPatsClient
{
    Task<PatsSystemInfo> GetSystemAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatsServerSummary>> ListServersAsync(CancellationToken cancellationToken = default);

    Task CreateServerAsync(Guid uuid, bool startOnCompletion = false, CancellationToken cancellationToken = default);

    Task DeleteServerAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task<PatsServerState?> GetServerAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task PowerAsync(Guid uuid, string action, int waitSeconds = 30, CancellationToken cancellationToken = default);

    Task SendCommandsAsync(Guid uuid, IReadOnlyList<string> commands, CancellationToken cancellationToken = default);

    Task InstallAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task ReinstallAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task SyncAsync(Guid uuid, CancellationToken cancellationToken = default);

    Task<string> GetLogsAsync(Guid uuid, int size = 100, CancellationToken cancellationToken = default);

    Task<string> ListDirectoryAsync(Guid uuid, string directory, CancellationToken cancellationToken = default);

    Task<string> ReadFileAsync(Guid uuid, string file, bool download = false, CancellationToken cancellationToken = default);

    Task WriteFileAsync(Guid uuid, string file, string contents, CancellationToken cancellationToken = default);

    Task RenameAsync(
        Guid uuid,
        string root,
        IReadOnlyList<(string From, string To)> files,
        CancellationToken cancellationToken = default);

    Task CopyAsync(Guid uuid, string location, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(Guid uuid, string name, string path, CancellationToken cancellationToken = default);

    Task DeleteFilesAsync(Guid uuid, string root, IReadOnlyList<string> files, CancellationToken cancellationToken = default);

    Task<string> CompressAsync(Guid uuid, string root, IReadOnlyList<string> files, CancellationToken cancellationToken = default);

    Task DecompressAsync(Guid uuid, string root, string file, CancellationToken cancellationToken = default);

    Task ChmodAsync(
        Guid uuid,
        string root,
        IReadOnlyList<(string File, string Mode)> files,
        CancellationToken cancellationToken = default);

    Task CreateBackupAsync(Guid uuid, Guid backupUuid, string adapter = "wings", CancellationToken cancellationToken = default);

    Task RestoreBackupAsync(Guid uuid, Guid backupUuid, bool truncateDirectory = false, CancellationToken cancellationToken = default);

    Task DeleteBackupAsync(Guid uuid, Guid backupUuid, CancellationToken cancellationToken = default);

    Task TransferAsync(
        Guid uuid,
        string targetUrl,
        string transferToken,
        Guid serverUuid,
        CancellationToken cancellationToken = default);

    Task CancelTransferAsync(Guid uuid, CancellationToken cancellationToken = default);
}

public interface IPatsClientFactory
{
    IPatsClient Create(Node node);
}
