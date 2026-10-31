using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;
using Rabbictyl.Core.Extensions;
using Tomlyn;

namespace Rabbictyl.Infrastructure.Extensions;

/// <summary>
/// Loads nests from TOML files. A nest defines its eggs inline, so a single file
/// describes a whole game family and can be versioned next to the panel.
/// </summary>
public sealed class FileSystemNestCatalog : INestCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _directory;
    private readonly ILogger<FileSystemNestCatalog> _logger;

    public FileSystemNestCatalog(string directory, ILogger<FileSystemNestCatalog> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NestDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var nests = new List<NestDescriptor>();

        foreach (var file in Directory.EnumerateFiles(_directory, "*.toml").OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var model = TomlSerializer.Deserialize<NestFile>(content, TomlOptions);

                if (model is null)
                {
                    continue;
                }

                var id = string.IsNullOrWhiteSpace(model.Id)
                    ? Path.GetFileNameWithoutExtension(file)
                    : model.Id.Trim();

                nests.Add(ToDescriptor(id, model));
            }
            catch (Exception exception) when (exception is TomlException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Could not read nest file {File}.", file);
            }
        }

        return nests;
    }

    private static NestDescriptor ToDescriptor(string id, NestFile model)
    {
        var name = string.IsNullOrWhiteSpace(model.Name) ? id : model.Name.Trim();
        var author = model.Author?.Trim() ?? string.Empty;

        var eggs = new List<Egg>();

        foreach (var eggFile in model.Egg)
        {
            if (string.IsNullOrWhiteSpace(eggFile.Id) || string.IsNullOrWhiteSpace(eggFile.Startup))
            {
                continue;
            }

            var eggId = eggFile.Id.Trim();

            eggs.Add(new Egg
            {
                Id = DeterministicGuid($"nest:{id}:{eggId}"),
                Uuid = DeterministicGuid($"nest-egg:{id}:{eggId}"),
                Name = string.IsNullOrWhiteSpace(eggFile.Name) ? eggId : eggFile.Name.Trim(),
                Description = eggFile.Description,
                Author = author,
                DockerImage = eggFile.DockerImage ?? string.Empty,
                DockerImagesJson = JsonSerializer.Serialize(BuildImages(eggFile), JsonOptions),
                StartupCommand = eggFile.Startup.Trim(),
                ProcessConfigurationJson = EggTemplate.BuildProcessConfiguration(
                    eggFile.Done ?? [],
                    eggFile.StopType ?? "command",
                    eggFile.StopValue ?? "stop"),
                ScriptContainer = string.IsNullOrWhiteSpace(eggFile.ScriptContainer)
                    ? "alpine:3.20"
                    : eggFile.ScriptContainer,
                ScriptEntrypoint = string.IsNullOrWhiteSpace(eggFile.ScriptEntrypoint)
                    ? "ash"
                    : eggFile.ScriptEntrypoint,
                ScriptInstall = eggFile.ScriptInstall,
                VariablesJson = EggTemplate.BuildVariables(eggFile.Variables ?? []),
            });
        }

        return new NestDescriptor(id, name, author, model.Description, eggs, model.Version?.Trim());
    }

    /// <summary>
    /// Builds the named image map from <c>docker_images</c>, where each entry is
    /// <c>image||Display name</c>. The plain <c>docker_image</c> stays the
    /// default unless the same image is already listed under a name.
    /// </summary>
    private static Dictionary<string, string> BuildImages(NestEggFile eggFile)
    {
        var pairs = new List<KeyValuePair<string, string>>();

        foreach (var entry in eggFile.DockerImages ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var separator = entry.IndexOf("||", StringComparison.Ordinal);
            var image = (separator >= 0 ? entry[..separator] : entry).Trim();
            var name = separator >= 0 ? entry[(separator + 2)..].Trim() : string.Empty;

            if (image.Length == 0)
            {
                continue;
            }

            if (name.Length == 0)
            {
                name = image;
            }

            if (pairs.Any(pair => string.Equals(pair.Key, name, StringComparison.Ordinal)))
            {
                continue;
            }

            pairs.Add(new KeyValuePair<string, string>(name, image));
        }

        var primary = eggFile.DockerImage?.Trim() ?? string.Empty;

        if (primary.Length > 0 && !pairs.Any(pair => string.Equals(pair.Value, primary, StringComparison.Ordinal)))
        {
            pairs.Insert(0, new KeyValuePair<string, string>("default", primary));
        }

        var images = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in pairs)
        {
            images[pair.Key] = pair.Value;
        }

        if (images.Count == 0)
        {
            images["default"] = primary;
        }

        return images;
    }

    /// <summary>
    /// Nest templates keep a stable UUID so servers that reference them survive a
    /// restart and a re-read of the TOML file.
    /// </summary>
    private static Guid DeterministicGuid(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private sealed class NestFile
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Author { get; set; }

        public string? Description { get; set; }

        /// <summary>Version of the nest file itself, e.g. <c>1.00</c>.</summary>
        public string? Version { get; set; }

        public List<NestEggFile> Egg { get; set; } = [];
    }

    private sealed class NestEggFile
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? DockerImage { get; set; }

        /// <summary>Named images as <c>image||Display name</c> entries.</summary>
        public List<string>? DockerImages { get; set; }

        public string? Startup { get; set; }

        public List<string>? Done { get; set; }

        public string? StopType { get; set; }

        public string? StopValue { get; set; }

        public Dictionary<string, string>? Variables { get; set; }

        public string? ScriptContainer { get; set; }

        public string? ScriptEntrypoint { get; set; }

        public string? ScriptInstall { get; set; }
    }
}
