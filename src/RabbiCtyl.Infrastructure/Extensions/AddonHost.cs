using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Addons.Abstractions;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Extensions;
using RabbiCtyl.Infrastructure.Maintenance;

namespace RabbiCtyl.Infrastructure.Extensions;

/// <summary>
/// Loads enabled C# addons from their manifests at host startup. Each addon
/// gets its own collectible load context so it can be unloaded on shutdown.
/// Addons can use the panel's stores, so loading waits for the database.
/// </summary>
public sealed class AddonHost : BackgroundService
{
    private readonly IAddonCatalog _catalog;
    private readonly IServiceProvider _services;
    private readonly MaintenanceState _maintenance;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AddonHost> _logger;
    private readonly List<LoadedAddon> _loaded = [];

    public AddonHost(
        IAddonCatalog catalog,
        IServiceProvider services,
        MaintenanceState maintenance,
        ILoggerFactory loggerFactory,
        ILogger<AddonHost> logger)
    {
        _catalog = catalog;
        _services = services;
        _maintenance = maintenance;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (_maintenance.Current.BlocksRequests)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
        }

        var addons = await _catalog.ListAsync(enabledOnly: true, stoppingToken);

        foreach (var addon in addons)
        {
            if (addon.AssemblyPath is null)
            {
                _logger.LogDebug("Addon '{Addon}' has no C# assembly; only its browser scripts are loaded.", addon.Id);
                continue;
            }

            try
            {
                await StartAddonAsync(addon, stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not start addon '{Addon}'.", addon.Id);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down.
        }

        await StopAddonsAsync(CancellationToken.None);
    }

    private async Task StopAddonsAsync(CancellationToken cancellationToken)
    {
        for (var index = _loaded.Count - 1; index >= 0; index--)
        {
            var loaded = _loaded[index];

            try
            {
                await loaded.Addon.ShutdownAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Addon '{Addon}' failed while shutting down.", loaded.Addon.Id);
            }
            finally
            {
                loaded.Context.Unload();
            }
        }

        _loaded.Clear();
    }

    private async Task StartAddonAsync(AddonDescriptor descriptor, CancellationToken cancellationToken)
    {
        var context = new AddonLoadContext(descriptor.AssemblyPath!);
        var assembly = context.LoadFromAssemblyPath(descriptor.AssemblyPath!);

        if (assembly.GetType(descriptor.Manifest.Entry!, throwOnError: false) is not { } entryType ||
            !typeof(IAddon).IsAssignableFrom(entryType))
        {
            throw new InvalidOperationException(
                $"Addon '{descriptor.Id}' entry type '{descriptor.Manifest.Entry}' does not implement IAddon.");
        }

        if (Activator.CreateInstance(entryType) is not IAddon addon)
        {
            throw new InvalidOperationException(
                $"Addon '{descriptor.Id}' entry type '{descriptor.Manifest.Entry}' could not be created.");
        }

        var addonContext = new AddonContext
        {
            Manifest = descriptor.Manifest,
            Directory = descriptor.Directory,
            Services = _services,
            Logger = _loggerFactory.CreateLogger($"Addon.{descriptor.Id}"),
        };

        await addon.InitializeAsync(addonContext, cancellationToken);

        _loaded.Add(new LoadedAddon(addon, context));
        _logger.LogInformation("Started addon '{Addon}' v{Version}.", descriptor.Id, descriptor.Manifest.Version);
    }

    private sealed record LoadedAddon(IAddon Addon, AddonLoadContext Context);
}
