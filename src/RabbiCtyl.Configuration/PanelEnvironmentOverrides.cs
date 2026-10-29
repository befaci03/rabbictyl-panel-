using System.Globalization;
using RabbiCtyl.Configuration.Options;

namespace RabbiCtyl.Configuration;

/// <summary>
/// Applies a small, documented set of environment overrides on top of the TOML
/// file. Secrets (such as the bootstrap password) should always come from here.
/// </summary>
public static class PanelEnvironmentOverrides
{
    public static void Apply(PanelOptions options, Func<string, string?>? read = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        read ??= Environment.GetEnvironmentVariable;

        Set(read("RABBICTYL_APP_NAME"), value => options.App.Name = value);
        Set(read("RABBICTYL_APP_URL"), value => options.App.Url = value);
        Set(read("RABBICTYL_APP_ENVIRONMENT"), value => options.App.Environment = value);
        Set(read("RABBICTYL_APP_DEBUG"), value => options.App.Debug = ParseBool(value));

        Set(read("RABBICTYL_SERVER_HOST"), value => options.Server.Host = value);
        Set(read("RABBICTYL_SERVER_PORT"), value => options.Server.Port = ParseInt(value));
        Set(read("RABBICTYL_SERVER_BEHIND_PROXY"), value => options.Server.BehindProxy = ParseBool(value));

        Set(read("RABBICTYL_DATABASE_PROVIDER"), value => options.Database.Provider = value);
        Set(read("RABBICTYL_DATABASE_CONNECTION_STRING"), value => options.Database.ConnectionString = value);
        Set(read("RABBICTYL_DATABASE_NAME"), value => options.Database.DatabaseName = value);
        Set(read("RABBICTYL_DATABASE_KEYSPACE"), value => options.Database.Keyspace = value);

        Set(read("RABBICTYL_CACHE_DRIVER"), value => options.Cache.Driver = value);
        Set(read("RABBICTYL_CACHE_CONNECTION_STRING"), value => options.Cache.ConnectionString = value);

        Set(read("RABBICTYL_SECURITY_COOKIE_NAME"), value => options.Security.CookieName = value);
        Set(read("RABBICTYL_SECURITY_SESSION_LIFETIME_MINUTES"),
            value => options.Security.SessionLifetimeMinutes = ParseInt(value));
        Set(read("RABBICTYL_SECURITY_BCRYPT_WORK_FACTOR"),
            value => options.Security.BcryptWorkFactor = ParseInt(value));
        Set(read("RABBICTYL_SECURITY_REQUIRE_HTTPS"), value => options.Security.RequireHttps = ParseBool(value));

        Set(read("RABBICTYL_LOGGING_LEVEL"), value => options.Logging.Level = value);

        Set(read("RABBICTYL_THEMES_DIRECTORY"), value => options.Themes.Directory = value);
        Set(read("RABBICTYL_THEMES_ACTIVE"), value => options.Themes.Active = value);
        Set(read("RABBICTYL_ADDONS_DIRECTORY"), value => options.Addons.Directory = value);
        Set(read("RABBICTYL_STORAGE_DIRECTORY"), value => options.Storage.Directory = value);

        Set(read("RABBICTYL_BOOTSTRAP_ADMIN_EMAIL"), value => options.Bootstrap.AdminEmail = value);
        Set(read("RABBICTYL_BOOTSTRAP_ADMIN_USERNAME"), value => options.Bootstrap.AdminUsername = value);
        Set(read("RABBICTYL_BOOTSTRAP_ADMIN_PASSWORD"), value => options.Bootstrap.AdminPassword = value);
    }

    private static void Set(string? rawValue, Action<string> apply)
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            apply(rawValue.Trim());
        }
    }

    private static bool ParseBool(string value) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw new ConfigurationException($"'{value}' is not a valid boolean.");

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ConfigurationException($"'{value}' is not a valid integer.");
}
