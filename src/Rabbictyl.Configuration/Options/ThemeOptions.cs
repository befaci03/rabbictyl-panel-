namespace Rabbictyl.Configuration.Options;

public sealed class ThemeOptions
{
    public string Directory { get; set; } = "themes";

    public string Active { get; set; } = "default_dark";
}
