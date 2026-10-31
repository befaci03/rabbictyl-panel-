namespace Rabbictyl.Configuration.Options;

public sealed class CacheOptions
{
    /// <summary>One of: memory, redis.</summary>
    public string Driver { get; set; } = "memory";

    public string ConnectionString { get; set; } = string.Empty;
}
