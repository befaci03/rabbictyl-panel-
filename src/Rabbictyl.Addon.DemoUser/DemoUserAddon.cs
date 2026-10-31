using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rabbictyl.Addons.Abstractions;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Addon.DemoUser;

/// <summary>
/// Seeds a single non-administrator demo account so a fresh panel can be tried
/// out. Nothing is written to the panel's nodes, eggs, allocations or servers:
/// the demo servers are simulated entirely in the browser by this add-on's
/// TypeScript script, which asks <c>/addons/demo_user/api/state</c> whether the
/// visitor is signed in as the demo account.
/// </summary>
public sealed class DemoUserAddon : IAddon, IAddonUserGuard, IAddonEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private DemoSettings _settings = new();

    public string Id => "demo_user";

    public async Task InitializeAsync(AddonContext context, CancellationToken cancellationToken = default)
    {
        _settings = DemoSettings.Load(context.Directory, context.Logger);

        using var scope = context.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();

        if (await users.FindByLoginAsync(_settings.Username, cancellationToken) is not null)
        {
            context.Logger.LogDebug("Demo account already exists; nothing to seed.");
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await users.AddAsync(
            new User
            {
                Username = _settings.Username,
                Email = _settings.Email,
                NameFirst = _settings.FirstName,
                NameLast = _settings.LastName,
                PasswordHash = hasher.Hash(_settings.Password),
                IsRootAdmin = false,
            },
            cancellationToken);

        context.Logger.LogInformation(
            "Created the demo account '{Username}' (password '{Password}').",
            _settings.Username,
            _settings.Password);
    }

    public string? ProtectionReason(User user) =>
        string.Equals(user.Username, _settings.Username, StringComparison.OrdinalIgnoreCase)
            ? "This account is managed by the Demo User add-on. Disable the add-on to edit or delete it."
            : null;

    /// <summary>
    /// Tells the browser script whether the signed-in account is the demo one,
    /// so the simulated servers only appear for the demo visitor.
    /// </summary>
    public Task<AddonResponse> HandleAsync(
        AddonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Path.Trim('/'), "state", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AddonResponse.NotFound());
        }

        var isDemo = request.Username is { Length: > 0 } username &&
            string.Equals(username, _settings.Username, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(new AddonResponse
        {
            Body = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    isDemoUser = isDemo,
                    username = _settings.Username,
                },
                JsonOptions),
            ContentType = "application/json",
        });
    }

    /// <summary>Optional <c>settings.json</c> shipped inside the addon folder.</summary>
    private sealed class DemoSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public string Username { get; set; } = "demo";

        public string Password { get; set; } = "demo";

        public string Email { get; set; } = "demo@example.com";

        public string FirstName { get; set; } = "Demo";

        public string LastName { get; set; } = "User";

        public static DemoSettings Load(string directory, ILogger logger)
        {
            var path = Path.Combine(directory, "settings.json");

            if (!File.Exists(path))
            {
                return new DemoSettings();
            }

            try
            {
                var settings = JsonSerializer.Deserialize<DemoSettings>(
                    File.ReadAllText(path),
                    JsonOptions) ?? new DemoSettings();

                if (string.IsNullOrWhiteSpace(settings.Username))
                {
                    settings.Username = "demo";
                }

                if (string.IsNullOrWhiteSpace(settings.Password))
                {
                    settings.Password = "demo";
                }

                return settings;
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Could not read the demo add-on settings; using defaults.");

                return new DemoSettings();
            }
        }
    }
}
