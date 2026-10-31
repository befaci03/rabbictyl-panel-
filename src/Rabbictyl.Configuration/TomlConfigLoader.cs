using System.Text.Json;
using Rabbictyl.Configuration.Options;
using Tomlyn;

namespace Rabbictyl.Configuration;

/// <summary>
/// Reads <c>panel.toml</c> into <see cref="PanelOptions"/>. Parsing is kept
/// separate from environment overrides so it can be unit tested deterministically.
/// </summary>
public static class TomlConfigLoader
{
    public const string DefaultFileName = "panel.toml";

    private static readonly TomlSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DuplicateKeyHandling = TomlDuplicateKeyHandling.Error,
    };

    public static PanelOptions Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new ConfigurationException(
                $"Configuration file not found at '{path}'. Copy config/panel.example.toml to config/panel.toml and try again.");
        }

        var options = Parse(File.ReadAllText(path), path);
        PanelEnvironmentOverrides.Apply(options);
        Validate(options);

        return options;
    }

    public static PanelOptions Parse(string toml, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        var serializerOptions = sourceName is null
            ? SerializerOptions
            : SerializerOptions with { SourceName = sourceName };

        try
        {
            var options = TomlSerializer.Deserialize<PanelOptions>(toml, serializerOptions);

            return options ?? throw new ConfigurationException("The configuration file is empty.");
        }
        catch (TomlException exception)
        {
            throw new ConfigurationException(
                $"Failed to parse configuration{FormatSource(sourceName)}: {exception.Message}", exception);
        }
    }

    public static void Validate(PanelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Server.Port is < 1 or > 65535)
        {
            throw new ConfigurationException(
                $"server.port must be between 1 and 65535, but was {options.Server.Port}.");
        }

        if (options.Security.SessionLifetimeMinutes <= 0)
        {
            throw new ConfigurationException("security.session_lifetime_minutes must be greater than zero.");
        }

        if (options.Security.BcryptWorkFactor is < 4 or > 31)
        {
            throw new ConfigurationException("security.bcrypt_work_factor must be between 4 and 31.");
        }

        var captcha = options.Security.Captcha.ResolveProvider();

        if (captcha != Options.CaptchaOptions.None
            && (string.IsNullOrWhiteSpace(options.Security.Captcha.SiteKey)
                || string.IsNullOrWhiteSpace(options.Security.Captcha.SecretKey)))
        {
            throw new ConfigurationException(
                $"security.captcha.{captcha} requires both site_key and secret_key.");
        }

        if (string.IsNullOrWhiteSpace(options.App.Name))
        {
            throw new ConfigurationException("app.name must not be empty.");
        }

        ValidateMail(options.Mail);

        _ = options.Database.ResolveProvider();
    }

    private static void ValidateMail(Options.MailOptions mail)
    {
        if (!mail.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(mail.Host))
        {
            throw new ConfigurationException("mail.host is required when mail.enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(mail.FromAddress))
        {
            throw new ConfigurationException("mail.from_address is required when mail.enabled is true.");
        }

        if (mail.Port is < 1 or > 65535)
        {
            throw new ConfigurationException("mail.port must be between 1 and 65535.");
        }

        _ = mail.ResolveEncryption();
    }

    private static string FormatSource(string? sourceName) =>
        string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $" '{sourceName}'";
}
