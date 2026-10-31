using Microsoft.Extensions.Logging;
using Rabbictyl.Configuration.Options;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure;

public sealed class AdminSeeder : IAdminSeeder
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly BootstrapOptions _options;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(
        IUserStore users,
        IPasswordHasher passwordHasher,
        BootstrapOptions options,
        ILogger<AdminSeeder> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _options = options;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.SeedAdmin)
        {
            return;
        }

        if (await _users.CountAsync(cancellationToken) > 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            _logger.LogWarning(
                "No users exist and bootstrap.admin_password is empty. Set RABBICTYL_BOOTSTRAP_ADMIN_PASSWORD to create the first administrator.");
            return;
        }

        if (_options.AdminPassword.Length < 8)
        {
            throw new InvalidOperationException("The bootstrap administrator password must be at least 8 characters long.");
        }

        var admin = new User
        {
            Username = _options.AdminUsername.Trim(),
            Email = _options.AdminEmail.Trim(),
            NameFirst = "Admin",
            NameLast = "User",
            PasswordHash = _passwordHasher.Hash(_options.AdminPassword),
            IsRootAdmin = true,
        };

        await _users.AddAsync(admin, cancellationToken);

        _logger.LogInformation("Created the initial administrator account '{Username}'.", admin.Username);
    }
}
