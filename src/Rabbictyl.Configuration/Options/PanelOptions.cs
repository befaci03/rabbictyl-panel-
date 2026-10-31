namespace Rabbictyl.Configuration.Options;

/// <summary>
/// Root of the TOML configuration file. Each section maps to a table in
/// <c>panel.toml</c> (for example <c>[database]</c> maps to <see cref="Database"/>).
/// </summary>
public sealed class PanelOptions
{
    public AppOptions App { get; set; } = new();

    public ServerOptions Server { get; set; } = new();

    public DatabaseOptions Database { get; set; } = new();

    public CacheOptions Cache { get; set; } = new();

    public SecurityOptions Security { get; set; } = new();

    public MailOptions Mail { get; set; } = new();

    public LoggingOptions Logging { get; set; } = new();

    public BootstrapOptions Bootstrap { get; set; } = new();

    public ThemeOptions Themes { get; set; } = new();

    public AddonOptions Addons { get; set; } = new();

    public NestOptions Nests { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();
}
