using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbiCtyl.Addons.Abstractions;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;
using RabbiCtyl.Core.Extensions;
using RabbiCtyl.Infrastructure.Maintenance;

namespace RabbiCtyl.Infrastructure.Extensions;

/// <summary>An administration tab provided by a running addon.</summary>
public sealed record AddonAdminTab(string Slug, string Label, string AddonId);

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
    private readonly PanelOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AddonHost> _logger;
    private readonly List<LoadedAddon> _loaded = [];
    private readonly object _gate = new();

    public AddonHost(
        IAddonCatalog catalog,
        IServiceProvider services,
        MaintenanceState maintenance,
        PanelOptions options,
        ILoggerFactory loggerFactory,
        ILogger<AddonHost> logger)
    {
        _catalog = catalog;
        _services = services;
        _maintenance = maintenance;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>Administration tabs contributed by the running addons.</summary>
    public IReadOnlyList<AddonAdminTab> AdminTabs
    {
        get
        {
            lock (_gate)
            {
                return _loaded
                    .Where(loaded => loaded.Addon is IAddonAdminPage)
                    .Select(loaded =>
                    {
                        var page = (IAddonAdminPage)loaded.Addon;
                        return new AddonAdminTab(page.Slug, page.Label, loaded.Descriptor.Id);
                    })
                    .ToList();
            }
        }
    }

    public bool IsAddonRunning(string addonId)
    {
        lock (_gate)
        {
            return _loaded.Any(loaded =>
                string.Equals(loaded.Descriptor.Id, addonId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public AddonAdminTab? FindAdminTab(string slug) =>
        AdminTabs.FirstOrDefault(tab => string.Equals(tab.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public Task<AddonResponse?> HandleAdminAsync(
        string slug,
        AddonRequest request,
        CancellationToken cancellationToken = default)
    {
        IAddonAdminPage? page;

        lock (_gate)
        {
            page = _loaded
                .Select(loaded => loaded.Addon)
                .OfType<IAddonAdminPage>()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase));
        }

        return page is null
            ? Task.FromResult<AddonResponse?>(null)
            : InvokeAsync(page.HandleAsync(request, cancellationToken));
    }

    public Task<AddonResponse?> HandleEndpointAsync(
        string addonId,
        AddonRequest request,
        CancellationToken cancellationToken = default)
    {
        IAddonEndpoint? endpoint;

        lock (_gate)
        {
            endpoint = _loaded
                .Where(loaded => string.Equals(loaded.Descriptor.Id, addonId, StringComparison.OrdinalIgnoreCase))
                .Select(loaded => loaded.Addon)
                .OfType<IAddonEndpoint>()
                .FirstOrDefault();
        }

        return endpoint is null
            ? Task.FromResult<AddonResponse?>(null)
            : InvokeAsync(endpoint.HandleAsync(request, cancellationToken));
    }

    /// <summary>
    /// True when the supplied account is locked by an addon (for example the
    /// demo account while the Demo User addon is enabled).
    /// </summary>
    public string? ProtectionReason(User user)
    {
        lock (_gate)
        {
            foreach (var loaded in _loaded)
            {
                if (loaded.Addon is IAddonUserGuard guard &&
                    guard.ProtectionReason(user) is { Length: > 0 } reason)
                {
                    return reason;
                }
            }
        }

        return null;
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
        LoadedAddon[] loaded;

        lock (_gate)
        {
            loaded = _loaded.ToArray();
            _loaded.Clear();
        }

        for (var index = loaded.Length - 1; index >= 0; index--)
        {
            var addon = loaded[index];

            try
            {
                await addon.Addon.ShutdownAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Addon '{Addon}' failed while shutting down.", addon.Addon.Id);
            }
            finally
            {
                addon.Context.Unload();
            }
        }
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

        var storageDirectory = Path.Combine(_options.Storage.Directory, "addons", descriptor.Id);

        try
        {
            Directory.CreateDirectory(storageDirectory);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not create the storage directory for addon '{Addon}'.", descriptor.Id);
        }

        var addonContext = new AddonContext
        {
            Manifest = descriptor.Manifest,
            Directory = descriptor.Directory,
            StorageDirectory = storageDirectory,
            Services = _services,
            Logger = _loggerFactory.CreateLogger($"Addon.{descriptor.Id}"),
        };

        await addon.InitializeAsync(addonContext, cancellationToken);

        lock (_gate)
        {
            _loaded.Add(new LoadedAddon(descriptor, addon, context));
        }

        _logger.LogInformation("Started addon '{Addon}' v{Version}.", descriptor.Id, descriptor.Manifest.Version);
    }

    private static async Task<AddonResponse?> InvokeAsync(Task<AddonResponse> task) => await task;

    private sealed record LoadedAddon(AddonDescriptor Descriptor, IAddon Addon, AddonLoadContext Context);
}
