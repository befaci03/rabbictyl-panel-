using System.Text.Json.Serialization;

namespace RabbiCtyl.Core.Extensions;

/// <summary>
/// Describes an addon folder. Addons may ship a C# plugin assembly and
/// TypeScript browser scripts, plus id-addressable assets. Plain JavaScript is
/// rejected on purpose.
/// </summary>
public sealed class AddonManifest
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

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional path to the compiled C# addon assembly, relative to the addon folder.</summary>
    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    /// <summary>Optional fully qualified type name implementing IAddon.</summary>
    [JsonPropertyName("entry")]
    public string? Entry { get; set; }

    [JsonPropertyName("scripts")]
    public List<string> Scripts { get; set; } = [];

    [JsonPropertyName("assets")]
    public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
