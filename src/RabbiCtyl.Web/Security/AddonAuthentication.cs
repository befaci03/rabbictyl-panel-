using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RabbiCtyl.Addons.Abstractions;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Security;

/// <summary>
/// The single place addons can turn a resolved account into a signed-in panel
/// session. Nothing here knows which addon is calling.
/// </summary>
public sealed class AddonAuthentication : IAddonAuthentication
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SecurityOptions _security;

    public AddonAuthentication(IHttpContextAccessor httpContextAccessor, SecurityOptions security)
    {
        _httpContextAccessor = httpContextAccessor;
        _security = security;
    }

    public bool AllowRegistration => _security.AllowRegistration;

    public async Task SignInAsync(User user, CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("There is no active request to sign in.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.IsRootAdmin ? "admin" : "user"),
            new("display_name", user.DisplayName),
        };

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("There is no active request to sign out.");

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
