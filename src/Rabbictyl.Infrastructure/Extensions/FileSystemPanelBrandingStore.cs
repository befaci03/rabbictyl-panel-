using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Infrastructure.Extensions;

/// <summary>
/// Stores the panel name, sidebar-logo visibility and uploaded logo in the
/// writable storage directory. The TOML configuration stays read-only.
/// </summary>
public sealed class FileSystemPanelBrandingStore : IPanelBrandingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _storageDirectory;
    private readonly string _stateFilePath;
    private readonly ILogger<FileSystemPanelBrandingStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private PanelBranding _current;

    public FileSystemPanelBrandingStore(
        string storageDirectory,
        string defaultName,
        ILogger<FileSystemPanelBrandingStore> logger)
    {
        _storageDirectory = storageDirectory;
        _stateFilePath = Path.Combine(storageDirectory, "panel.json");
        _logger = logger;
        _current = new PanelBranding { Name = defaultName };

        TryLoad();
    }

    public PanelBranding Current => Volatile.Read(ref _current);

    public string? LogoPath
    {
        get
        {
            var fileName = Current.LogoFileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var path = Path.Combine(_storageDirectory, fileName);

            return File.Exists(path) ? path : null;
        }
    }

    public long LogoVersion =>
        LogoPath is { } path ? File.GetLastWriteTimeUtc(path).Ticks : 0;

    public Task SaveAsync(PanelBranding branding, CancellationToken cancellationToken = default) =>
        PersistAsync(
            new PanelBranding
            {
                Name = string.IsNullOrWhiteSpace(branding.Name) ? Current.Name : branding.Name.Trim(),
                HideSidebarLogo = branding.HideSidebarLogo,
                MaintenanceMode = branding.MaintenanceMode,
                MaintenanceReason = string.IsNullOrWhiteSpace(branding.MaintenanceReason)
                    ? null
                    : branding.MaintenanceReason.Trim(),
                LogoFileName = Current.LogoFileName,
            },
            cancellationToken);

    public async Task SaveLogoAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_storageDirectory);

        var fileName = $"panel-logo{extension.ToLowerInvariant()}";
        var path = Path.Combine(_storageDirectory, fileName);

        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        if (Current.LogoFileName is { Length: > 0 } previous &&
            !string.Equals(previous, fileName, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(Path.Combine(_storageDirectory, previous));
        }

        await PersistAsync(
            new PanelBranding
            {
                Name = Current.Name,
                HideSidebarLogo = Current.HideSidebarLogo,
                MaintenanceMode = Current.MaintenanceMode,
                MaintenanceReason = Current.MaintenanceReason,
                LogoFileName = fileName,
            },
            cancellationToken);
    }

    public async Task RemoveLogoAsync(CancellationToken cancellationToken = default)
    {
        if (Current.LogoFileName is { Length: > 0 } fileName)
        {
            TryDelete(Path.Combine(_storageDirectory, fileName));
        }

        await PersistAsync(
            new PanelBranding
            {
                Name = Current.Name,
                HideSidebarLogo = Current.HideSidebarLogo,
                MaintenanceMode = Current.MaintenanceMode,
                MaintenanceReason = Current.MaintenanceReason,
                LogoFileName = null,
            },
            cancellationToken);
    }

    private async Task PersistAsync(PanelBranding branding, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_storageDirectory);

            var temporaryPath = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(branding, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, _stateFilePath, overwrite: true);

            Volatile.Write(ref _current, branding);
            _logger.LogInformation("Updated panel branding to '{Name}'.", branding.Name);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void TryLoad()
    {
        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        try
        {
            var branding = JsonSerializer.Deserialize<PanelBranding>(
                File.ReadAllText(_stateFilePath), JsonOptions);

            if (branding is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(branding.Name))
            {
                _current.Name = branding.Name.Trim();
            }

            _current.HideSidebarLogo = branding.HideSidebarLogo;
            _current.MaintenanceMode = branding.MaintenanceMode;
            _current.MaintenanceReason = branding.MaintenanceReason;
            _current.LogoFileName = branding.LogoFileName;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Could not read the persisted panel branding; using defaults.");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not delete the previous panel logo '{Path}'.", path);
        }
    }
}
