namespace Rabbictyl.Configuration.Options;

/// <summary>
/// SMTP settings used for panel email (password resets today, anything else that
/// needs to reach a user later).
/// </summary>
public sealed class MailOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    /// <summary><c>none</c>, <c>starttls</c> or <c>ssl</c>.</summary>
    public string Encryption { get; set; } = "starttls";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Rabbict_yl";

    public int TimeoutSeconds { get; set; } = 15;

    public string ResolveEncryption()
    {
        var encryption = (Encryption ?? string.Empty).Trim().ToLowerInvariant();

        return encryption switch
        {
            "" or "none" => "none",
            "starttls" or "tls" => "starttls",
            "ssl" or "smtps" => "ssl",
            _ => throw new ConfigurationException(
                $"mail.encryption '{Encryption}' is not supported. Use 'none', 'starttls' or 'ssl'."),
        };
    }
}
