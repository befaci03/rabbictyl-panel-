using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Infrastructure.Extensions;

/// <summary>
/// Persists per-addon enable/disable overrides next to the theme state. The
/// manifest default is used until an administrator flips the switch.
/// </summary>
public sealed class FileSystemAddonState : IAddonState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _stateFilePath;
    private readonly ILogger<FileSystemAddonState> _logger;
    private readonly ConcurrentDictionary<string, bool> _overrides = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileSystemAddonState(string stateFilePath, ILogger<FileSystemAddonState> logger)
    {
        _stateFilePath = stateFilePath;
        _logger = logger;

        TryLoadPersistedState();
    }

    public bool? GetOverride(string addonId) =>
        _overrides.TryGetValue(addonId, out var enabled) ? enabled : null;

    public async Task SetEnabledAsync(
        string addonId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        _overrides[addonId] = enabled;

        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _stateFilePath + ".tmp";
            var document = new AddonStateDocument
            {
                Disabled = _overrides
                    .Where(pair => !pair.Value)
                    .Select(pair => pair.Key)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList(),
                Enabled = _overrides
                    .Where(pair => pair.Value)
                    .Select(pair => pair.Key)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList(),
            };

            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(document, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, _stateFilePath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void TryLoadPersistedState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<AddonStateDocument>(
                File.ReadAllText(_stateFilePath), JsonOptions);

            foreach (var id in document?.Enabled ?? [])
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _overrides[id] = true;
                }
            }

            foreach (var id in document?.Disabled ?? [])
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _overrides[id] = false;
                }
            }
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Could not read the persisted addon state; using manifest defaults.");
        }
    }

    private sealed class AddonStateDocument
    {
        public List<string> Enabled { get; set; } = [];

        public List<string> Disabled { get; set; } = [];
    }
}
