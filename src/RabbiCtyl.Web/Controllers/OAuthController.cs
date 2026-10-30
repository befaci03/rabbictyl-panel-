using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Web.Security;

namespace RabbiCtyl.Web.Controllers;

/// <summary>
/// Server-side OAuth2 authorization-code flow. A provider is only usable while
/// the <c>oauth2</c> add-on is enabled; the provider settings themselves are
/// managed from Administration → OAuth2.
/// </summary>
[AllowAnonymous]
[Route("auth/oauth")]
public sealed class OAuthController : Controller
{
    private const string AddonId = "oauth2";
    private const string StateCookiePrefix = "rabbictyl_oauth_state_";

    private readonly IOAuthProviderStore _providers;
    private readonly IAddonCatalog _addons;
    private readonly IUserStore _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PanelOptions _options;
    private readonly SecurityOptions _security;
    private readonly ILogger<OAuthController> _logger;

    public OAuthController(
        IOAuthProviderStore providers,
        IAddonCatalog addons,
        IUserStore users,
        IPasswordHasher passwordHasher,
        IHttpClientFactory httpClientFactory,
        PanelOptions options,
        SecurityOptions security,
        ILogger<OAuthController> logger)
    {
        _providers = providers;
        _addons = addons;
        _users = users;
        _passwordHasher = passwordHasher;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _security = security;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Challenge(string id, CancellationToken cancellationToken)
    {
        var provider = await ResolveAsync(id, cancellationToken);

        if (provider is null)
        {
            TempData["Error"] = "That sign-in provider is not available.";

            return RedirectToAction("Login", "Auth");
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        Response.Cookies.Append(
            StateCookie(provider.Id),
            state,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = _security.RequireHttps,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddMinutes(10),
            });

        var authorize = new UriBuilder(provider.AuthorizeUrl);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = RedirectUri(provider.Id),
            ["response_type"] = "code",
            ["scope"] = provider.Scopes,
            ["state"] = state,
        };

        var existing = authorize.Query.TrimStart('?');
        var added = string.Join('&', query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        authorize.Query = string.IsNullOrWhiteSpace(existing) ? added : $"{existing}&{added}";

        return Redirect(authorize.Uri.ToString());
    }

    [HttpGet("{id}/callback")]
    public async Task<IActionResult> Callback(
        string id,
        string? code,
        string? state,
        string? error,
        string? error_description,
        CancellationToken cancellationToken)
    {
        var provider = await ResolveAsync(id, cancellationToken);

        if (provider is null)
        {
            TempData["Error"] = "That sign-in provider is not available.";

            return RedirectToAction("Login", "Auth");
        }

        var expectedState = Request.Cookies[StateCookie(provider.Id)];
        Response.Cookies.Delete(StateCookie(provider.Id), new CookieOptions { Path = "/" });

        if (!string.IsNullOrWhiteSpace(error))
        {
            TempData["Error"] = error_description ?? "The provider refused the sign-in.";

            return RedirectToAction("Login", "Auth");
        }

        if (string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(expectedState)
            || !string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            TempData["Error"] = "The sign-in request expired. Try again.";

            return RedirectToAction("Login", "Auth");
        }

        try
        {
            var profile = await FetchProfileAsync(provider, code, cancellationToken);

            if (string.IsNullOrWhiteSpace(profile.Email))
            {
                TempData["Error"] = $"{provider.Name} did not return an email address for this account.";

                return RedirectToAction("Login", "Auth");
            }

            var user = await _users.FindByLoginAsync(profile.Email, cancellationToken);

            if (user is null)
            {
                if (!_security.AllowRegistration)
                {
                    TempData["Error"] = $"No panel account uses {profile.Email}. Ask an administrator to create one.";

                    return RedirectToAction("Login", "Auth");
                }

                var username = await UniqueUsernameAsync(profile.Email, profile.Username, cancellationToken);

                user = new User
                {
                    Username = username,
                    Email = profile.Email,
                    NameFirst = FirstName(profile.DisplayName, provider.Name),
                    NameLast = LastName(profile.DisplayName),
                    PasswordHash = _passwordHasher.Hash(
                        Convert.ToHexString(RandomNumberGenerator.GetBytes(24))),
                    IsRootAdmin = false,
                };

                await _users.AddAsync(user, cancellationToken);

                _logger.LogInformation(
                    "Created account '{Username}' from {Provider} OAuth2 sign-in.",
                    user.Username,
                    provider.Name);
            }

            await SignInAsync(user);

            return RedirectToAction("Index", "Home");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "OAuth2 sign-in with {Provider} failed.", provider.Name);
            TempData["Error"] = "Could not complete the sign-in with the provider. Try again.";

            return RedirectToAction("Login", "Auth");
        }
    }

    private async Task<OAuthProvider?> ResolveAsync(string id, CancellationToken cancellationToken)
    {
        var enabledAddons = await _addons.ListAsync(enabledOnly: true, cancellationToken: cancellationToken);

        if (!enabledAddons.Any(addon =>
                string.Equals(addon.Id, AddonId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var provider = await _providers.FindAsync(id, cancellationToken);

        return provider is { Enabled: true } && provider.IsConfigured ? provider : null;
    }

    private string RedirectUri(string id) =>
        $"{_options.App.Url.TrimEnd('/')}{Url.Action(nameof(Callback), "OAuth", new { id })}";

    private async Task<OAuthProfile> FetchProfileAsync(
        OAuthProvider provider,
        string code,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("oauth");

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, provider.TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri(provider.Id),
                ["client_id"] = provider.ClientId,
                ["client_secret"] = provider.ClientSecret,
            }),
        };

        tokenRequest.Headers.Accept.ParseAdd("application/json");

        using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The token endpoint returned HTTP {(int)tokenResponse.StatusCode}.");
        }

        using var tokenDocument = JsonDocument.Parse(
            await tokenResponse.Content.ReadAsStringAsync(cancellationToken));

        if (!tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("The token endpoint did not return an access token.");
        }

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, provider.UserInfoUrl);

        userRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenElement.GetString());
        userRequest.Headers.Accept.ParseAdd("application/json");

        using var userResponse = await client.SendAsync(userRequest, cancellationToken);

        if (!userResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The user info endpoint returned HTTP {(int)userResponse.StatusCode}.");
        }

        using var profileDocument = JsonDocument.Parse(
            await userResponse.Content.ReadAsStringAsync(cancellationToken));

        var root = profileDocument.RootElement;

        return new OAuthProfile(
            Subject: ReadString(root, provider.UserIdField),
            Email: ReadString(root, provider.EmailField),
            Username: ReadString(root, provider.UsernameField),
            DisplayName: ReadString(root, provider.DisplayNameField));
    }

    private static string? ReadString(JsonElement root, string property)
    {
        if (string.IsNullOrWhiteSpace(property)
            || !root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    private async Task<string> UniqueUsernameAsync(
        string email,
        string? preferred,
        CancellationToken cancellationToken)
    {
        var source = string.IsNullOrWhiteSpace(preferred)
            ? email.Split('@')[0]
            : preferred;

        var cleaned = new string(source
            .ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            .ToArray());

        if (cleaned.Length < 3)
        {
            cleaned = $"user{cleaned}";
        }

        cleaned = cleaned[..Math.Min(cleaned.Length, 28)];

        var username = cleaned;
        var suffix = 1;

        while (await _users.FindByLoginAsync(username, cancellationToken) is not null)
        {
            username = $"{cleaned}{suffix++}";

            if (username.Length > 32)
            {
                username = username[..32];
            }
        }

        return username;
    }

    private static string FirstName(string? displayName, string fallback)
    {
        var parts = (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length > 0 ? parts[0] : fallback;
    }

    private static string LastName(string? displayName)
    {
        var parts = (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length > 1 ? string.Join(' ', parts[1..]) : string.Empty;
    }

    private async Task SignInAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.IsRootAdmin ? "admin" : "user"),
            new("display_name", user.DisplayName),
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });

        _logger.LogInformation("User '{Username}' signed in with OAuth2.", user.Username);
    }

    private static string StateCookie(string id) => $"{StateCookiePrefix}{id}";

    private sealed record OAuthProfile(
        string? Subject,
        string? Email,
        string? Username,
        string? DisplayName);
}
