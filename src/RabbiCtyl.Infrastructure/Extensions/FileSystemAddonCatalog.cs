using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Infrastructure.Extensions;

public sealed class FileSystemAddonCatalog : IAddonCatalog
{
    private readonly string _rootDirectory;
    private readonly IAddonState _state;
    private readonly ILogger<FileSystemAddonCatalog> _logger;

    public FileSystemAddonCatalog(
        string rootDirectory,
        IAddonState state,
        ILogger<FileSystemAddonCatalog> logger)
    {
        _rootDirectory = rootDirectory;
        _state = state;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AddonDescriptor>> ListAsync(
        bool enabledOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            _logger.LogWarning("Addons directory '{Directory}' does not exist.", _rootDirectory);
            return [];
        }

        var addons = new List<AddonDescriptor>();

        foreach (var directory in Directory
                     .EnumerateDirectories(_rootDirectory)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var descriptor = await LoadAsync(directory, cancellationToken);

            if (descriptor is not null && (!enabledOnly || descriptor.IsEnabled))
            {
                addons.Add(descriptor);
            }
        }

        return addons;
    }

    public async Task<AddonDescriptor?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!ExtensionPath.IsValidId(id))
        {
            return null;
        }

        var directory = ExtensionPath.ResolveWithin(_rootDirectory, id);

        return directory is null || !Directory.Exists(directory)
            ? null
            : await LoadAsync(directory, cancellationToken);
    }

    public async Task<ExtensionAsset?> FindAssetAsync(
        string id,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        var descriptor = await FindAsync(id, cancellationToken);

        if (descriptor is null ||
            !descriptor.Manifest.Assets.TryGetValue(assetId, out var relativePath))
        {
            return null;
        }

        var fullPath = ExtensionPath.ResolveWithin(descriptor.Directory, relativePath);

        return fullPath is null || !File.Exists(fullPath)
            ? null
            : new ExtensionAsset(fullPath, ExtensionPath.ContentTypeFor(fullPath), Path.GetFileName(fullPath));
    }

    private async Task<AddonDescriptor?> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        var folderName = Path.GetFileName(directory);

        try
        {
            var manifest = await ExtensionManifestReader.ReadAsync<AddonManifest>(
                Path.Combine(directory, "manifest.json"), cancellationToken);

            if (manifest is null)
            {
                throw new InvalidDataException("manifest.json is missing.");
            }

            if (!ExtensionPath.IsValidId(manifest.Id) ||
                !string.Equals(manifest.Id, folderName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The manifest id '{manifest.Id}' must match the folder name '{folderName}'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new InvalidDataException("The manifest name must not be empty.");
            }

            // An administrator override always wins over the shipped manifest.
            if (_state.GetOverride(manifest.Id) is { } enabled)
            {
                manifest.Enabled = enabled;
            }

            var scripts = BuildScripts(manifest, directory);
            var assets = BuildAssets(manifest, directory);
            var assemblyPath = ResolveAssembly(manifest, directory);

            return new AddonDescriptor(manifest, directory, assemblyPath, scripts, assets);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            _logger.LogWarning(exception, "Skipping invalid addon '{Addon}'.", folderName);
            return null;
        }
    }

    private static List<string> BuildScripts(AddonManifest manifest, string directory)
    {
        var urls = new List<string>();

        foreach (var script in manifest.Scripts)
        {
            if (!string.Equals(Path.GetExtension(script), ".ts", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Addon '{manifest.Id}' script '{script}' must be TypeScript (.ts). JavaScript is not supported.");
            }

            EnsureFileExists(directory, script, $"addon '{manifest.Id}' script");

            var relative = Path.ChangeExtension(script.Replace('\\', '/'), ".js");
            urls.Add($"/addons/{manifest.Id}/{relative}");
        }

        return urls;
    }

    private static Dictionary<string, string> BuildAssets(AddonManifest manifest, string directory)
    {
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (assetId, relativePath) in manifest.Assets)
        {
            if (!ExtensionPath.IsValidId(assetId))
            {
                throw new InvalidDataException($"Addon '{manifest.Id}' has an invalid asset id '{assetId}'.");
            }

            EnsureFileExists(directory, relativePath, $"addon '{manifest.Id}' asset '{assetId}'");
            urls[assetId] = $"/api/addons/{manifest.Id}/assets/{assetId}";
        }

        return urls;
    }

    private static string? ResolveAssembly(AddonManifest manifest, string directory)
    {
        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            if (!string.IsNullOrWhiteSpace(manifest.Entry))
            {
                throw new InvalidDataException(
                    $"Addon '{manifest.Id}' declares an entry point but no assembly.");
            }

            return null;
        }

        if (!string.Equals(Path.GetExtension(manifest.Assembly), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Addon '{manifest.Id}' assembly must be a .dll file.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry))
        {
            throw new InvalidDataException(
                $"Addon '{manifest.Id}' declares an assembly but no entry point.");
        }

        var fullPath = ExtensionPath.ResolveWithin(directory, manifest.Assembly);

        if (fullPath is null || !File.Exists(fullPath))
        {
            throw new InvalidDataException(
                $"Addon '{manifest.Id}' assembly '{manifest.Assembly}' does not exist.");
        }

        return fullPath;
    }

    private static void EnsureFileExists(string directory, string relativePath, string description)
    {
        var fullPath = ExtensionPath.ResolveWithin(directory, relativePath);

        if (fullPath is null || !File.Exists(fullPath))
        {
            throw new InvalidDataException(
                $"The {description} file '{relativePath}' does not exist or escapes the addon folder.");
        }
    }
}
