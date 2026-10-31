namespace Rabbictyl.Configuration.Options;

/// <summary>
/// Human check shown on the sign-in form. Pterodactyl supports both Cloudflare
/// Turnstile and hCaptcha; this panel does the same and keeps the provider
/// switchable from <c>panel.toml</c>.
/// </summary>
public sealed class CaptchaOptions
{
    public const string None = "none";
    public const string Turnstile = "turnstile";
    public const string HCaptcha = "hcaptcha";

    /// <summary>One of <c>none</c>, <c>turnstile</c> or <c>hcaptcha</c>.</summary>
    public string Provider { get; set; } = None;

    /// <summary>Public site key handed to the browser widget.</summary>
    public string SiteKey { get; set; } = string.Empty;

    /// <summary>Secret key used to verify the token server side.</summary>
    public string SecretKey { get; set; } = string.Empty;

    public bool Enabled => ResolveProvider() is not None;

    public string ResolveProvider()
    {
        var provider = (Provider ?? string.Empty).Trim().ToLowerInvariant();

        return provider switch
        {
            "" or None => None,
            Turnstile => Turnstile,
            HCaptcha => HCaptcha,
            _ => throw new ConfigurationException(
                $"security.captcha.provider '{Provider}' is not supported. Use 'none', 'turnstile' or 'hcaptcha'."),
        };
    }
}
