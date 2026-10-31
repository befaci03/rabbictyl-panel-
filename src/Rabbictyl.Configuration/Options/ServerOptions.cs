namespace Rabbictyl.Configuration.Options;

public sealed class ServerOptions
{
    public string Host { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 8080;

    public bool BehindProxy { get; set; }
}
