using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Security;

/// <summary>
/// Application API keys live next to the other panel state
/// (<c>storage/api_keys.json</c>) so they behave identically on SQL, MongoDB
/// and Cassandra deployments and are easy to back up.
/// </summary>
public sealed class FileSystemApiKeyStore : IApiKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly ILogger<FileSystemApiKeyStore> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public FileSystemApiKeyStore(string path, ILogger<FileSystemApiKeyStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            return (await ReadAsync(cancellationToken))
                .OrderBy(key => key.CreatedAt)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ApiKey?> FindAsync(string identifier, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            return (await ReadAsync(cancellationToken))
                .FirstOrDefault(key => string.Equals(key.Identifier, identifier, StringComparison.Ordinal));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task AddAsync(ApiKey key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            var keys = await ReadAsync(cancellationToken);
            keys.Add(key);
            await WriteAsync(keys, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task DeleteAsync(ApiKey key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            var keys = await ReadAsync(cancellationToken);
            keys.RemoveAll(existing => existing.Id == key.Id);
            await WriteAsync(keys, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<List<ApiKey>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);

            return JsonSerializer.Deserialize<List<ApiKey>>(json, JsonOptions) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogWarning(exception, "Could not read API keys from {Path}.", _path);

            return [];
        }
    }

    private async Task WriteAsync(List<ApiKey> keys, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            _path,
            JsonSerializer.Serialize(keys, JsonOptions),
            cancellationToken);
    }
}
