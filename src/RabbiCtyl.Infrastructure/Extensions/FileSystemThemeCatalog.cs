using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Infrastructure.Extensions;

public sealed class FileSystemThemeCatalog : IThemeCatalog
{
    private static readonly HashSet<string> StyleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".css", ".scss", ".sass" };

    private readonly string _rootDirectory;
    private readonly ILogger<FileSystemThemeCatalog> _logger;

    public FileSystemThemeCatalog(string rootDirectory, ILogger<FileSystemThemeCatalog> logger)
    {
        _rootDirectory = rootDirectory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ThemeDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            _logger.LogWarning("Themes directory '{Directory}' does not exist.", _rootDirectory);
            return [];
        }

        var themes = new List<ThemeDescriptor>();

        foreach (var directory in Directory
                     .EnumerateDirectories(_rootDirectory)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var descriptor = await LoadAsync(directory, cancellationToken);

            if (descriptor is not null)
            {
                themes.Add(descriptor);
            }
        }

        return themes;
    }

    public async Task<ThemeDescriptor?> FindAsync(string id, CancellationToken cancellationToken = default)
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

        var directory = ExtensionPath.ResolveWithin(_rootDirectory, id);

        if (directory is null)
        {
            return null;
        }

        var fullPath = ExtensionPath.ResolveWithin(directory, relativePath);

        return fullPath is null || !File.Exists(fullPath)
            ? null
            : new ExtensionAsset(fullPath, ExtensionPath.ContentTypeFor(fullPath), Path.GetFileName(fullPath));
    }

    private async Task<ThemeDescriptor?> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        var folderName = Path.GetFileName(directory);

        try
        {
            var manifest = await ExtensionManifestReader.ReadAsync<ThemeManifest>(
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

            var styles = BuildStyles(manifest, directory);
            var scripts = BuildScripts(manifest, directory);
            var assets = BuildAssets(manifest, directory);

            return new ThemeDescriptor(manifest, styles, scripts, assets);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            _logger.LogWarning(exception, "Skipping invalid theme '{Theme}'.", folderName);
            return null;
        }
    }

    private static List<string> BuildStyles(ThemeManifest manifest, string directory)
    {
        var urls = new List<string>();

        foreach (var style in manifest.Styles)
        {
            var extension = Path.GetExtension(style);

            if (!StyleExtensions.Contains(extension))
            {
                throw new InvalidDataException(
                    $"Theme '{manifest.Id}' style '{style}' must be .css, .scss or .sass.");
            }

            EnsureFileExists(directory, style, $"theme '{manifest.Id}' style");

            var relative = Path.ChangeExtension(style.Replace('\\', '/'), ".css");
            urls.Add($"/themes/{manifest.Id}/{relative}");
        }

        return urls;
    }

    private static List<string> BuildScripts(ThemeManifest manifest, string directory)
    {
        var urls = new List<string>();

        foreach (var script in manifest.Scripts)
        {
            if (!string.Equals(Path.GetExtension(script), ".ts", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Theme '{manifest.Id}' script '{script}' must be TypeScript (.ts).");
            }

            EnsureFileExists(directory, script, $"theme '{manifest.Id}' script");

            var relative = Path.ChangeExtension(script.Replace('\\', '/'), ".js");
            urls.Add($"/themes/{manifest.Id}/{relative}");
        }

        return urls;
    }

    private static Dictionary<string, string> BuildAssets(ThemeManifest manifest, string directory)
    {
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (assetId, relativePath) in manifest.Assets)
        {
            if (!ExtensionPath.IsValidId(assetId))
            {
                throw new InvalidDataException($"Theme '{manifest.Id}' has an invalid asset id '{assetId}'.");
            }

            EnsureFileExists(directory, relativePath, $"theme '{manifest.Id}' asset '{assetId}'");
            urls[assetId] = $"/api/themes/{manifest.Id}/assets/{assetId}";
        }

        return urls;
    }

    private static void EnsureFileExists(string directory, string relativePath, string description)
    {
        var fullPath = ExtensionPath.ResolveWithin(directory, relativePath);

        if (fullPath is null || !File.Exists(fullPath))
        {
            throw new InvalidDataException($"The {description} file '{relativePath}' does not exist or escapes the theme folder.");
        }
    }
}
