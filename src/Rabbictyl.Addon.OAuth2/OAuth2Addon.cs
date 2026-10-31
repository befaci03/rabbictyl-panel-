using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rabbictyl.Addons.Abstractions;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Addon.OAuth2;

/// <summary>
/// The whole OAuth2 feature lives here: provider settings, the authorization
/// code flow and the administration page. The panel only provides generic
/// services (sign-in bridge, HTTP client, user store); there is no OAuth2 code
/// in the panel itself.
/// </summary>
public sealed partial class OAuth2Addon : IAddon, IAddonAdminPage, IAddonEndpoint
{
    private const string AdminRoute = "/admin/addons/oauth2";
    private const string ApiRoute = "/addons/oauth2/api";

    private readonly Dictionary<string, PendingLogin> _pending = new(StringComparer.Ordinal);
    private readonly object _pendingGate = new();

    private AddonContext? _context;
    private string _providersPath = string.Empty;

    public string Id => "oauth2";

    public string Slug => "oauth2";

    public string Label => "OAuth2";

    public Task InitializeAsync(AddonContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _providersPath = Path.Combine(context.StorageDirectory, "providers.json");

        return Task.CompletedTask;
    }

    public Task<AddonResponse> HandleAsync(
        AddonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_context is null)
        {
            return Task.FromResult(
                AddonResponse.Page("<div class=\"alert alert--danger\">The OAuth2 add-on is still starting.</div>"));
        }

        return request.Path.ToLowerInvariant() switch
        {
            "providers" => ProvidersAsync(cancellationToken),
            "start" => StartAsync(request, cancellationToken),
            "callback" => CallbackAsync(request, cancellationToken),
            _ => AdminAsync(request, cancellationToken),
        };
    }

    // Sign-in -------------------------------------------------------------------

    private async Task<AddonResponse> ProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = (await ReadProvidersAsync(cancellationToken))
            .Where(provider => provider.Enabled && provider.IsConfigured)
            .Select(provider => new { id = provider.Id, name = provider.Name })
            .ToList();

        return new AddonResponse
        {
            Body = JsonSerializer.SerializeToUtf8Bytes(providers),
            ContentType = "application/json",
        };
    }

    private async Task<AddonResponse> StartAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        var providerId = QueryValue(request, "provider");
        var provider = (await ReadProvidersAsync(cancellationToken))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, providerId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Enabled &&
                candidate.IsConfigured);

        if (provider is null)
        {
            return Failure("That sign-in provider is not available.");
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        lock (_pendingGate)
        {
            foreach (var expired in _pending
                         .Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _pending.Remove(expired);
            }

            _pending[state] = new PendingLogin(provider.Id, DateTimeOffset.UtcNow.AddMinutes(10));
        }

        var authorize = new UriBuilder(provider.AuthorizeUrl);
        var parameters = string.Join('&', new Dictionary<string, string>
        {
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = RedirectUri(request, provider.Id),
            ["response_type"] = "code",
            ["scope"] = provider.Scopes,
            ["state"] = state,
        }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var existing = authorize.Query.TrimStart('?');
        authorize.Query = string.IsNullOrWhiteSpace(existing) ? parameters : $"{existing}&{parameters}";

        return new AddonResponse { Redirect = authorize.Uri.ToString() };
    }

    private async Task<AddonResponse> CallbackAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryTakeState(QueryValue(request, "state"), out var pending))
        {
            return Failure("The sign-in request expired. Try again.");
        }

        if (QueryValue(request, "error") is { Length: > 0 } error)
        {
            var description = QueryValue(request, "error_description");

            return Failure(description.Length > 0 ? description : "The provider refused the sign-in.");
        }

        var providerId = QueryValue(request, "provider");

        if (providerId.Length == 0)
        {
            providerId = pending.ProviderId;
        }

        var provider = (await ReadProvidersAsync(cancellationToken))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (provider is null || !provider.IsConfigured)
        {
            return Failure("That sign-in provider is not available.");
        }

        var code = QueryValue(request, "code");

        if (code.Length == 0)
        {
            return Failure("The provider did not return an authorization code.");
        }

        Profile profile;

        try
        {
            profile = await FetchProfileAsync(
                provider,
                code,
                RedirectUri(request, provider.Id),
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Failure("Could not complete the sign-in with the provider. Try again.");
        }

        if (string.IsNullOrWhiteSpace(profile.Email))
        {
            return Failure($"{provider.Name} did not return an email address for this account.");
        }

        using var scope = _context!.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var authentication = scope.ServiceProvider.GetRequiredService<IAddonAuthentication>();
        var user = await users.FindByLoginAsync(profile.Email, cancellationToken);

        if (user is null)
        {
            if (!authentication.AllowRegistration)
            {
                return Failure($"No panel account uses {profile.Email}. Ask an administrator to create one.");
            }

            var username = await UniqueUsernameAsync(
                users,
                profile.Email,
                profile.Username,
                cancellationToken);

            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            user = new User
            {
                Username = username,
                Email = profile.Email,
                NameFirst = FirstName(profile.DisplayName, provider.Name),
                NameLast = LastName(profile.DisplayName),
                PasswordHash = hasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(24))),
            };

            await users.AddAsync(user, cancellationToken);
        }

        await authentication.SignInAsync(user, cancellationToken);

        return new AddonResponse { Redirect = "/" };
    }

    private async Task<Profile> FetchProfileAsync(
        Provider provider,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        // The add-on owns its HTTP calls; nothing about the provider leaks into the panel.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Rabbictyl-OAuth2/1.0");

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, provider.TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
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

        if (!tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement) ||
            tokenElement.ValueKind != JsonValueKind.String)
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

        return new Profile(
            Email: ReadString(root, provider.EmailField),
            Username: ReadString(root, provider.UsernameField),
            DisplayName: ReadString(root, provider.DisplayNameField));
    }

    private bool TryTakeState(string state, out PendingLogin pending)
    {
        lock (_pendingGate)
        {
            if (state.Length > 0 && _pending.TryGetValue(state, out var found))
            {
                _pending.Remove(state);
                pending = found;

                return found.Expires > DateTimeOffset.UtcNow;
            }
        }

        pending = null!;

        return false;
    }

    private static string RedirectUri(AddonRequest request, string providerId) =>
        $"{request.BaseUrl.TrimEnd('/')}{ApiRoute}/callback?provider={Uri.EscapeDataString(providerId)}";

    private static string? ReadString(JsonElement root, string property)
    {
        if (string.IsNullOrWhiteSpace(property) || !root.TryGetProperty(property, out var value))
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

    private static async Task<string> UniqueUsernameAsync(
        IUserStore users,
        string email,
        string? preferred,
        CancellationToken cancellationToken)
    {
        var source = string.IsNullOrWhiteSpace(preferred) ? email.Split('@')[0] : preferred;

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

        while (await users.FindByLoginAsync(username, cancellationToken) is not null)
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

    private static AddonResponse Failure(string message) =>
        AddonResponse.Page(
            "<section class=\"auth\"><div class=\"auth__shell\"><div class=\"auth__form-side\"><div class=\"auth__form-inner\">" +
            $"<div class=\"alert alert--danger\" role=\"alert\">{E(message)}</div>" +
            "<p class=\"auth__note\"><a class=\"button button--ghost\" href=\"/auth/login\">Back to sign in</a></p>" +
            "</div></div></div></section>");

    // Administration ------------------------------------------------------------

    private async Task<AddonResponse> AdminAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Method == AddonHttpMethod.Post)
        {
            return await HandleAdminPostAsync(request, cancellationToken);
        }

        return Render(await ReadProvidersAsync(cancellationToken), request, error: null);
    }

    private async Task<AddonResponse> HandleAdminPostAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        return FormValue(request, "action") switch
        {
            "create" => await CreateAsync(request, cancellationToken),
            "update" => await UpdateAsync(request, cancellationToken),
            "delete" => await DeleteAsync(request, cancellationToken),
            _ => Render(
                await ReadProvidersAsync(cancellationToken),
                request,
                "Unknown action. Reload the page and try again."),
        };
    }

    private async Task<AddonResponse> CreateAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        var id = FormValue(request, "Id").ToLowerInvariant();
        var name = FormValue(request, "Name");

        if (!SlugPattern().IsMatch(id))
        {
            return Render(await ReadProvidersAsync(cancellationToken), request,
                "The provider id must be a lowercase slug such as 'github' or 'discord'.");
        }

        if (name.Length == 0)
        {
            return Render(await ReadProvidersAsync(cancellationToken), request,
                "A display name is required.");
        }

        var providers = await ReadProvidersAsync(cancellationToken);

        if (providers.Any(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return Render(providers, request, $"A provider with the id '{id}' already exists.");
        }

        var created = new Provider { Id = id };

        Apply(request, created, keepSecret: false);
        providers.Add(created);
        await WriteProvidersAsync(providers, cancellationToken);

        return Redirect($"Added the {created.Name} sign-in provider.");
    }

    private async Task<AddonResponse> UpdateAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        var id = FormValue(request, "ProviderId");
        var providers = await ReadProvidersAsync(cancellationToken);
        var provider = providers.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Render(providers, request, $"The provider '{id}' no longer exists.");
        }

        Apply(request, provider, keepSecret: true);
        await WriteProvidersAsync(providers, cancellationToken);

        return Redirect($"Updated the {provider.Name} sign-in provider.");
    }

    private async Task<AddonResponse> DeleteAsync(
        AddonRequest request,
        CancellationToken cancellationToken)
    {
        var id = FormValue(request, "ProviderId");
        var providers = await ReadProvidersAsync(cancellationToken);
        var provider = providers.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Render(providers, request, $"The provider '{id}' no longer exists.");
        }

        providers.Remove(provider);
        await WriteProvidersAsync(providers, cancellationToken);

        return Redirect($"Removed the {provider.Name} sign-in provider.");
    }

    private static void Apply(AddonRequest request, Provider provider, bool keepSecret)
    {
        provider.Name = FormValue(request, "Name");
        provider.ClientId = FormValue(request, "ClientId");

        if (!keepSecret || FormValue(request, "ClientSecret").Length > 0)
        {
            provider.ClientSecret = FormValue(request, "ClientSecret");
        }

        provider.AuthorizeUrl = FormValue(request, "AuthorizeUrl");
        provider.TokenUrl = FormValue(request, "TokenUrl");
        provider.UserInfoUrl = FormValue(request, "UserInfoUrl");
        provider.Scopes = FormValue(request, "Scopes");
        provider.UserIdField = FormValue(request, "UserIdField");
        provider.EmailField = FormValue(request, "EmailField");
        provider.UsernameField = FormValue(request, "UsernameField");
        provider.DisplayNameField = FormValue(request, "DisplayNameField");
        provider.Enabled = request.Form.ContainsKey("Enabled");
    }

    private static AddonResponse Redirect(string message) =>
        new()
        {
            Redirect = AdminRoute,
            Flash = message,
        };

    private AddonResponse Render(
        IReadOnlyList<Provider> providers,
        AddonRequest request,
        string? error)
    {
        var csrf = E(request.CsrfToken ?? string.Empty);
        var html = new System.Text.StringBuilder();

        html.AppendLine("<p class=\"form__hint\">Server-side OAuth2 sign-in, owned entirely by this add-on. Removing the addon folder removes the feature.</p>");

        if (!string.IsNullOrWhiteSpace(error))
        {
            html.AppendLine($"<div class=\"alert alert--danger\" role=\"alert\">{E(error)}</div>");
        }

        html.AppendLine(providers.Count == 0
            ? "<details class=\"card admin-create\" open>"
            : "<details class=\"card admin-create\">");
        html.AppendLine("<summary class=\"admin-create__summary\">");
        html.AppendLine("<span><strong>New provider</strong><span class=\"admin-create__hint\">Register the callback URL with the provider before enabling it.</span></span>");
        html.AppendLine("<span class=\"button button--primary button--sm\">Add provider</span>");
        html.AppendLine("</summary>");
        html.AppendLine("<div class=\"admin-create__body\">");
        html.AppendLine($"<form method=\"post\" action=\"{E(AdminRoute)}\" class=\"form\">");
        html.AppendLine($"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{csrf}\" />");
        html.AppendLine("<input type=\"hidden\" name=\"action\" value=\"create\" />");
        html.AppendLine("<div class=\"admin-create__grid\">");

        Input(html, "oauth-id", "Provider id", "Id", string.Empty, required: true, placeholder: "github",
            hint: "Lowercase slug used in the callback URL.");
        Input(html, "oauth-name", "Display name", "Name", string.Empty, required: true, placeholder: "GitHub");
        Input(html, "oauth-client", "Client ID", "ClientId", string.Empty, required: true);
        Input(html, "oauth-secret", "Client secret", "ClientSecret", string.Empty, required: true, type: "password");
        Input(html, "oauth-authorize", "Authorize URL", "AuthorizeUrl", string.Empty, required: true, type: "url",
            placeholder: "https://github.com/login/oauth/authorize");
        Input(html, "oauth-token", "Token URL", "TokenUrl", string.Empty, required: true, type: "url",
            placeholder: "https://github.com/login/oauth/access_token");
        Input(html, "oauth-userinfo", "User info URL", "UserInfoUrl", string.Empty, required: true, type: "url",
            placeholder: "https://api.github.com/user");
        Input(html, "oauth-scopes", "Scopes", "Scopes", "openid email profile");
        Input(html, "oauth-user-id", "User id field", "UserIdField", "id");
        Input(html, "oauth-email", "Email field", "EmailField", "email");
        Input(html, "oauth-username", "Username field", "UsernameField", "username");
        Input(html, "oauth-display", "Display name field", "DisplayNameField", "name");

        html.AppendLine("</div>");
        html.AppendLine("<div class=\"admin-create__checks\"><label class=\"form__check\"><input type=\"checkbox\" name=\"Enabled\" value=\"true\" checked /> Enabled</label></div>");
        html.AppendLine("<div class=\"admin-create__actions\"><button type=\"submit\" class=\"button button--primary\">Add provider</button></div>");
        html.AppendLine("</form></div></details>");
        html.AppendLine("<section class=\"card card--flush\">");

        if (providers.Count == 0)
        {
            html.AppendLine("<div class=\"empty\"><p class=\"empty__title\">No providers configured</p><p class=\"empty__hint\">Add a provider so users can sign in with it.</p></div>");
        }
        else
        {
            html.AppendLine("<div class=\"table-wrap\"><table class=\"table\"><thead><tr>");
            html.AppendLine("<th>Provider</th><th>Callback URL</th><th>Status</th><th></th>");
            html.AppendLine("</tr></thead><tbody>");

            foreach (var provider in providers)
            {
                var callback = $"{request.BaseUrl.TrimEnd('/')}{ApiRoute}/callback?provider={provider.Id}";
                var badge = provider is { Enabled: true, IsConfigured: true }
                    ? "<span class=\"badge badge--running\">Active</span>"
                    : provider.Enabled
                        ? "<span class=\"badge badge--suspended\">Incomplete</span>"
                        : "<span class=\"badge\">Disabled</span>";

                html.AppendLine("<tr>");
                html.AppendLine($"<td><span class=\"table__primary\">{E(provider.Name)}</span><span class=\"table__secondary\">{E(provider.Id)}</span></td>");
                html.AppendLine($"<td><code class=\"table__secondary\">{E(callback)}</code></td>");
                html.AppendLine($"<td>{badge}</td>");
                html.AppendLine("<td><div class=\"table__actions\">");

                html.AppendLine("<details class=\"node-config\">");
                html.AppendLine("<summary class=\"button button--ghost button--sm\">Edit</summary>");
                html.AppendLine("<div class=\"node-config__body\">");
                html.AppendLine($"<form method=\"post\" action=\"{E(AdminRoute)}\" class=\"form\">");
                html.AppendLine($"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{csrf}\" />");
                html.AppendLine("<input type=\"hidden\" name=\"action\" value=\"update\" />");
                html.AppendLine($"<input type=\"hidden\" name=\"ProviderId\" value=\"{E(provider.Id)}\" />");
                html.AppendLine("<div class=\"admin-create__grid\">");

                Input(html, $"edit-name-{provider.Id}", "Display name", "Name", provider.Name, required: true);
                Input(html, $"edit-client-{provider.Id}", "Client ID", "ClientId", provider.ClientId, required: true);
                Input(html, $"edit-secret-{provider.Id}", "Client secret", "ClientSecret", string.Empty, type: "password",
                    placeholder: "Leave blank to keep the current secret");
                Input(html, $"edit-scopes-{provider.Id}", "Scopes", "Scopes", provider.Scopes);
                Input(html, $"edit-authorize-{provider.Id}", "Authorize URL", "AuthorizeUrl", provider.AuthorizeUrl, required: true, type: "url");
                Input(html, $"edit-token-{provider.Id}", "Token URL", "TokenUrl", provider.TokenUrl, required: true, type: "url");
                Input(html, $"edit-userinfo-{provider.Id}", "User info URL", "UserInfoUrl", provider.UserInfoUrl, required: true, type: "url");
                Input(html, $"edit-user-id-{provider.Id}", "User id field", "UserIdField", provider.UserIdField);
                Input(html, $"edit-email-{provider.Id}", "Email field", "EmailField", provider.EmailField);
                Input(html, $"edit-username-{provider.Id}", "Username field", "UsernameField", provider.UsernameField);
                Input(html, $"edit-display-{provider.Id}", "Display name field", "DisplayNameField", provider.DisplayNameField);

                html.AppendLine("</div>");
                html.AppendLine("<div class=\"admin-create__checks\">");
                html.AppendLine($"<label class=\"form__check\"><input type=\"checkbox\" name=\"Enabled\" value=\"true\"{(provider.Enabled ? " checked" : string.Empty)} /> Enabled</label>");
                html.AppendLine("</div>");
                html.AppendLine("<div class=\"admin-create__actions\"><button type=\"submit\" class=\"button button--primary button--sm\">Save changes</button></div>");
                html.AppendLine("</form></div></details>");

                html.AppendLine($"<form method=\"post\" action=\"{E(AdminRoute)}\" class=\"inline-form\">");
                html.AppendLine($"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{csrf}\" />");
                html.AppendLine("<input type=\"hidden\" name=\"action\" value=\"delete\" />");
                html.AppendLine($"<input type=\"hidden\" name=\"ProviderId\" value=\"{E(provider.Id)}\" />");
                html.AppendLine("<button type=\"submit\" class=\"button button--ghost button--sm\">Delete</button>");
                html.AppendLine("</form>");

                html.AppendLine("</div></td></tr>");
            }

            html.AppendLine("</tbody></table></div>");
        }

        html.AppendLine("</section>");

        return AddonResponse.Page(html.ToString());
    }

    private static void Input(
        System.Text.StringBuilder html,
        string id,
        string label,
        string name,
        string value,
        bool required = false,
        string type = "text",
        string? placeholder = null,
        string? hint = null)
    {
        var attributes = new System.Text.StringBuilder();
        attributes.Append($" type=\"{E(type)}\"");
        attributes.Append($" id=\"{E(id)}\"");

        if (required)
        {
            attributes.Append(" required");
        }

        if (!string.IsNullOrEmpty(placeholder))
        {
            attributes.Append($" placeholder=\"{E(placeholder)}\"");
        }

        html.AppendLine("<div class=\"form__group\">");
        html.AppendLine($"<label class=\"form__label\" for=\"{E(id)}\">{E(label)}</label>");
        html.AppendLine($"<input class=\"form__input\" name=\"{E(name)}\" value=\"{E(value)}\"{attributes} />");

        if (!string.IsNullOrEmpty(hint))
        {
            html.AppendLine($"<span class=\"form__hint\">{E(hint)}</span>");
        }

        html.AppendLine("</div>");
    }

    // Storage -------------------------------------------------------------------

    private async Task<List<Provider>> ReadProvidersAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_providersPath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_providersPath, cancellationToken);

            return JsonSerializer.Deserialize<List<Provider>>(json) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return [];
        }
    }

    private async Task WriteProvidersAsync(List<Provider> providers, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_context!.StorageDirectory);

        await File.WriteAllTextAsync(
            _providersPath,
            JsonSerializer.Serialize(providers, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static string FormValue(AddonRequest request, string name) =>
        request.Form.TryGetValue(name, out var value) ? value.Trim() : string.Empty;

    private static string QueryValue(AddonRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value.Trim() : string.Empty;

    private static string E(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    // Public so System.Text.Json can deserialize the add-on's own provider file.
    public sealed class Provider
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        public string AuthorizeUrl { get; set; } = string.Empty;

        public string TokenUrl { get; set; } = string.Empty;

        public string UserInfoUrl { get; set; } = string.Empty;

        public string Scopes { get; set; } = "openid email profile";

        public string UserIdField { get; set; } = "id";

        public string EmailField { get; set; } = "email";

        public string UsernameField { get; set; } = "username";

        public string DisplayNameField { get; set; } = "name";

        public bool Enabled { get; set; } = true;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ClientSecret) &&
            !string.IsNullOrWhiteSpace(AuthorizeUrl) &&
            !string.IsNullOrWhiteSpace(TokenUrl) &&
            !string.IsNullOrWhiteSpace(UserInfoUrl);
    }

    private sealed record PendingLogin(string ProviderId, DateTimeOffset Expires);

    private sealed record Profile(string? Email, string? Username, string? DisplayName);
}
