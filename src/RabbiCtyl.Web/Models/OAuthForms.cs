using System.ComponentModel.DataAnnotations;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Models;

public sealed class OAuthAdminViewModel
{
    public IReadOnlyList<OAuthProvider> Providers { get; init; } = [];

    public bool AddonEnabled { get; init; }
}

public sealed class OAuthProviderForm
{
    [Required, StringLength(48, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9][a-z0-9_-]*$", ErrorMessage = "Use lowercase letters, digits, dashes or underscores.")]
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(96, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string ClientId { get; set; } = string.Empty;

    [Required, StringLength(512)]
    public string ClientSecret { get; set; } = string.Empty;

    [Required, Url, StringLength(1024)]
    public string AuthorizeUrl { get; set; } = string.Empty;

    [Required, Url, StringLength(1024)]
    public string TokenUrl { get; set; } = string.Empty;

    [Required, Url, StringLength(1024)]
    public string UserInfoUrl { get; set; } = string.Empty;

    [StringLength(256)]
    public string Scopes { get; set; } = "openid email profile";

    [StringLength(64)]
    public string UserIdField { get; set; } = "id";

    [StringLength(64)]
    public string EmailField { get; set; } = "email";

    [StringLength(64)]
    public string UsernameField { get; set; } = "username";

    [StringLength(64)]
    public string DisplayNameField { get; set; } = "name";

    public bool Enabled { get; set; } = true;
}

public sealed class OAuthProviderEditForm
{
    [Required, StringLength(96, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Leave blank to keep the stored secret.</summary>
    [StringLength(512)]
    public string? ClientSecret { get; set; }

    [Required, Url, StringLength(1024)]
    public string AuthorizeUrl { get; set; } = string.Empty;

    [Required, Url, StringLength(1024)]
    public string TokenUrl { get; set; } = string.Empty;

    [Required, Url, StringLength(1024)]
    public string UserInfoUrl { get; set; } = string.Empty;

    [StringLength(256)]
    public string Scopes { get; set; } = "openid email profile";

    [StringLength(64)]
    public string UserIdField { get; set; } = "id";

    [StringLength(64)]
    public string EmailField { get; set; } = "email";

    [StringLength(64)]
    public string UsernameField { get; set; } = "username";

    [StringLength(64)]
    public string DisplayNameField { get; set; } = "name";

    public bool Enabled { get; set; } = true;
}
