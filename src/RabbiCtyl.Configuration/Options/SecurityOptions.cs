namespace RabbiCtyl.Configuration.Options;

public sealed class SecurityOptions
{
    public string CookieName { get; set; } = "rabbictyl_session";

    public int SessionLifetimeMinutes { get; set; } = 120;

    public int BcryptWorkFactor { get; set; } = 12;

    public bool RequireHttps { get; set; }

    /// <summary>Allows visitors to create their own account from the sign-in page.</summary>
    public bool AllowRegistration { get; set; }

    public CaptchaOptions Captcha { get; set; } = new();
}
