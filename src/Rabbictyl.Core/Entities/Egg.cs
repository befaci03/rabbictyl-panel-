using System.Text.Json.Serialization;

namespace Rabbictyl.Core.Entities;

public sealed class Egg : Entity
{
    public Guid Uuid { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Author { get; set; } = string.Empty;

    /// <summary>Deprecated single image; prefer <see cref="DockerImagesJson"/>.</summary>
    public string DockerImage { get; set; } = string.Empty;

    /// <summary>JSON object of image label to image reference.</summary>
    public string DockerImagesJson { get; set; } = "{}";

    public string StartupCommand { get; set; } = string.Empty;

    public bool ForceOutgoingIp { get; set; }

    public string FileDenylistJson { get; set; } = "[]";

    /// <summary>JSON emitted directly as pats' <c>process_configuration</c>.</summary>
    public string ProcessConfigurationJson { get; set; } = "{}";

    public string ScriptContainer { get; set; } = "alpine:3.20";

    public string ScriptEntrypoint { get; set; } = "ash";

    public string? ScriptInstall { get; set; }

    /// <summary>
    /// Egg variables as a JSON array of <see cref="EggVariable"/> shaped objects.
    /// Storing the list next to the egg keeps every provider identical.
    /// </summary>
    public string VariablesJson { get; set; } = "[]";

    [JsonIgnore]
    public ICollection<EggVariable> Variables { get; set; } = new List<EggVariable>();
}
