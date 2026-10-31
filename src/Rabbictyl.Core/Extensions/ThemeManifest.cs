using System.Text.Json.Serialization;

namespace Rabbictyl.Core.Extensions;

/// <summary>
/// Describes a theme folder. Styles may be CSS, SCSS or SASS; scripts must be
/// TypeScript. Assets are addressed by id so templates never depend on paths.
/// </summary>
public sealed class ThemeManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// Optional built-in layout family. "pterodactyl" selects the panel's
    /// Pterodactyl-style main and auth layouts; null uses the default layouts.
    /// </summary>
    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("styles")]
    public List<string> Styles { get; set; } = [];

    [JsonPropertyName("scripts")]
    public List<string> Scripts { get; set; } = [];

    [JsonPropertyName("assets")]
    public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
