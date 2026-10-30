using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Addons.Abstractions;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Addon.DemoUser;

/// <summary>
/// Seeds a single non-administrator demo account so a fresh panel can be tried
/// out. The addon ships disabled; enabling it and restarting the panel creates
/// <c>demo</c> / <c>demo</c> if it does not exist yet.
/// </summary>
public sealed class DemoUserAddon : IAddon
{
    private const string Username = "demo";
    private const string Password = "demo";

    public string Id => "demo_user";

    public async Task InitializeAsync(AddonContext context, CancellationToken cancellationToken = default)
    {
        using var scope = context.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();

        if (await users.FindByLoginAsync(Username, cancellationToken) is not null)
        {
            context.Logger.LogDebug("Demo account already exists; nothing to seed.");
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await users.AddAsync(
            new User
            {
                Username = Username,
                Email = "demo@example.com",
                NameFirst = "Demo",
                NameLast = "User",
                PasswordHash = hasher.Hash(Password),
                IsRootAdmin = false,
            },
            cancellationToken);

        context.Logger.LogInformation(
            "Created the demo account '{Username}' (password '{Password}').",
            Username,
            Password);
    }
}
