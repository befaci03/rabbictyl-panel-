using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Core;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Maintenance;

/// <summary>
/// Creates or migrates the schema in the background, tracking progress in
/// <see cref="MaintenanceState"/>. The web app starts immediately so requests
/// during a migration get the maintenance page instead of a broken UI.
/// </summary>
public sealed class DatabaseInitializationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly MaintenanceState _maintenance;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        IServiceScopeFactory scopes,
        MaintenanceState maintenance,
        ILogger<DatabaseInitializationService> logger)
    {
        _scopes = scopes;
        _maintenance = maintenance;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        try
        {
            var states = services.GetRequiredService<IPanelStateStore>();
            var initializer = services.GetRequiredService<IDatabaseInitializer>();
            var seeder = services.GetRequiredService<IAdminSeeder>();

            var state = await states.GetAsync(stoppingToken);
            var target = PanelSchema.CurrentVersion;

            if (state is not null && state.Version > target)
            {
                _maintenance.MarkFailed(
                    $"The database schema version {state.Version} is newer than this panel ({target}). Upgrade the panel before starting it.",
                    state.Version);

                _logger.LogError(
                    "The database schema version {Stored} is newer than this panel ({Target}).",
                    state.Version,
                    target);

                return;
            }

            if (state is not null && state.Version < target)
            {
                // The exact message the operator sees on the console while the
                // schema is being upgraded.
                Console.WriteLine("migration in progress of your database...");
                _logger.LogWarning(
                    "migration in progress of your database... (schema {From} -> {To})",
                    state.Version,
                    target);

                _maintenance.MarkMigrating(
                    state.Version,
                    target,
                    "Applying database migrations. The panel will reopen automatically.");
            }

            await initializer.InitializeAsync(stoppingToken);

            state ??= new PanelState();
            state.Version = target;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            state.MigrationStartedAt = null;
            state.MigrationEtaSeconds = 0;
            await states.SaveAsync(state, stoppingToken);

            await seeder.SeedAsync(stoppingToken);

            _maintenance.MarkReady();

            _logger.LogInformation("Database is ready (schema version {Version}).", target);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; nothing to report.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Database initialization failed.");

            _maintenance.MarkFailed(exception.Message);
        }
    }
}
