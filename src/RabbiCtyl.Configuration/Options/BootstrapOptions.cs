namespace RabbiCtyl.Configuration.Options;

public sealed class BootstrapOptions
{
    public bool SeedAdmin { get; set; } = true;

    public string AdminEmail { get; set; } = "admin@example.com";

    public string AdminUsername { get; set; } = "admin";

    /// <summary>
    /// Optional. When empty the panel reads <c>RABBICTYL_BOOTSTRAP_ADMIN_PASSWORD</c>
    /// and otherwise skips seeding.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;
}
