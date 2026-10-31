using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Infrastructure.Extensions;

public sealed class FileSystemThemeState : IThemeState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _stateFilePath;
    private readonly IThemeCatalog _catalog;
    private readonly ILogger<FileSystemThemeState> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private string _activeThemeId;

    public FileSystemThemeState(
        string stateFilePath,
        string defaultThemeId,
        IThemeCatalog catalog,
        ILogger<FileSystemThemeState> logger)
    {
        _stateFilePath = stateFilePath;
        _catalog = catalog;
        _logger = logger;
        _activeThemeId = defaultThemeId;

        TryLoadPersistedTheme();
    }

    public string ActiveThemeId => Volatile.Read(ref _activeThemeId);

    public async Task ActivateAsync(string themeId, CancellationToken cancellationToken = default)
    {
        var descriptor = await _catalog.FindAsync(themeId, cancellationToken);

        if (descriptor is null)
        {
            throw new InvalidOperationException($"Theme '{themeId}' is not installed.");
        }

        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(new ThemeStateDocument { Active = themeId }, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, _stateFilePath, overwrite: true);

            Volatile.Write(ref _activeThemeId, themeId);
            _logger.LogInformation("Activated theme '{Theme}'.", themeId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void TryLoadPersistedTheme()
    {
        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<ThemeStateDocument>(
                File.ReadAllText(_stateFilePath), JsonOptions);

            if (!string.IsNullOrWhiteSpace(document?.Active))
            {
                _activeThemeId = document.Active;
            }
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Could not read the persisted theme state; using the default theme.");
        }
    }

    private sealed class ThemeStateDocument
    {
        public string Active { get; set; } = string.Empty;
    }
}
