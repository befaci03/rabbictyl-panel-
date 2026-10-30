using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Addons.Abstractions;

/// <summary>
/// Generic sign-in bridge the panel provides to addons. An addon that
/// implements its own authentication (OAuth2, LDAP, …) calls this instead of
/// the panel knowing anything about that provider.
/// </summary>
public interface IAddonAuthentication
{
    /// <summary>True when the panel allows self-service account creation.</summary>
    bool AllowRegistration { get; }

    Task SignInAsync(User user, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
