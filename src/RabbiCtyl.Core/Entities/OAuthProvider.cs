namespace RabbiCtyl.Core.Entities;

/// <summary>
/// An OAuth2/OIDC provider the panel can sign users in with. Providers are
/// managed from Administration → OAuth2 and stored under the panel's storage
/// directory, so they do not depend on the database provider.
/// </summary>
public sealed class OAuthProvider
{
    /// <summary>Slug used in the callback URL: <c>/auth/oauth/{id}</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string AuthorizeUrl { get; set; } = string.Empty;

    public string TokenUrl { get; set; } = string.Empty;

    public string UserInfoUrl { get; set; } = string.Empty;

    /// <summary>Space separated scopes; <c>scope</c> uses this instead of <c>scope</c>.</summary>
    public string Scopes { get; set; } = "openid email profile";

    /// <summary>JSON property holding the provider's stable user id.</summary>
    public string UserIdField { get; set; } = "id";

    public string EmailField { get; set; } = "email";

    public string UsernameField { get; set; } = "username";

    public string DisplayNameField { get; set; } = "name";

    public bool Enabled { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(AuthorizeUrl)
        && !string.IsNullOrWhiteSpace(TokenUrl)
        && !string.IsNullOrWhiteSpace(UserInfoUrl);
}
