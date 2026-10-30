using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Security;

/// <summary>
/// Provider settings live next to the other add-on state (<c>storage/oauth2.json</c>)
/// so they are independent of the selected database provider and easy to back up.
/// </summary>
public sealed class FileSystemOAuthProviderStore : IOAuthProviderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly ILogger<FileSystemOAuthProviderStore> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public FileSystemOAuthProviderStore(string path, ILogger<FileSystemOAuthProviderStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OAuthProvider>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            return (await ReadAsync(cancellationToken))
                .OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<OAuthProvider?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            return (await ReadAsync(cancellationToken))
                .FirstOrDefault(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveAsync(OAuthProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);

        await _mutex.WaitAsync(cancellationToken);

        try
        {
            var providers = await ReadAsync(cancellationToken);
            var index = providers.FindIndex(
                existing => string.Equals(existing.Id, provider.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                providers[index] = provider;
            }
            else
            {
                providers.Add(provider);
            }

            await WriteAsync(providers, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            var providers = await ReadAsync(cancellationToken);

            providers.RemoveAll(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));

            await WriteAsync(providers, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<List<OAuthProvider>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);

            return JsonSerializer.Deserialize<List<OAuthProvider>>(json, JsonOptions) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            _logger.LogWarning(exception, "Could not read OAuth2 provider settings from {Path}.", _path);

            return [];
        }
    }

    private async Task WriteAsync(List<OAuthProvider> providers, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            _path,
            JsonSerializer.Serialize(providers, JsonOptions),
            cancellationToken);
    }
}
