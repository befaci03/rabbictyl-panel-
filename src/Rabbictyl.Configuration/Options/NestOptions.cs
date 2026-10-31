namespace Rabbictyl.Configuration.Options;

/// <summary>Folder holding nest files. Each <c>*.toml</c> file is one nest.</summary>
public sealed class NestOptions
{
    public string Directory { get; set; } = "nests";
}
