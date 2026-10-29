namespace RabbiCtyl.Configuration.Options;

public sealed class SecurityOptions
{
    public string CookieName { get; set; } = "rabbictyl_session";

    public int SessionLifetimeMinutes { get; set; } = 120;

    public int BcryptWorkFactor { get; set; } = 12;

    public bool RequireHttps { get; set; }
}
