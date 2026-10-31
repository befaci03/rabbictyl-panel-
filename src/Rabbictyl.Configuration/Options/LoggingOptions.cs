namespace Rabbictyl.Configuration.Options;

public sealed class LoggingOptions
{
    /// <summary>Trace, Debug, Information, Warning, Error, Critical or None.</summary>
    public string Level { get; set; } = "Information";
}
