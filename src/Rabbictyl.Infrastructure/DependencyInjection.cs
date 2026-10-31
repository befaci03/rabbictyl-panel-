using Cassandra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Rabbictyl.Configuration.Options;
using Rabbictyl.Core.Abstractions;
using Rabbictyl.Infrastructure.Extensions;
using Rabbictyl.Infrastructure.Maintenance;
using Rabbictyl.Infrastructure.Pats;
using Rabbictyl.Infrastructure.Persistence.Cassandra;
using Rabbictyl.Infrastructure.Persistence.Mongo;
using Rabbictyl.Infrastructure.Persistence.Relational;
using Rabbictyl.Infrastructure.Security;

namespace Rabbictyl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbictylInfrastructure(
        this IServiceCollection services,
        PanelOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(options.Database);
        services.AddSingleton(options.Security);
        services.AddSingleton(options.Bootstrap);
        services.AddSingleton(options.Mail);
        services.AddMemoryCache();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher>(_ => new BcryptPasswordHasher(options.Security.BcryptWorkFactor));
        // Depends on the scoped stores (and the relational DbContext), so it must
        // live in the same scope the startup initializer creates.
        services.AddScoped<IAdminSeeder, AdminSeeder>();

        services.AddHttpClient("pats", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("captcha", client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient("oauth", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Rabbictyl/1.0");
        });
        services.AddSingleton<IPatsClientFactory, PatsClientFactory>();
        services.AddSingleton<ICaptchaVerifier, CaptchaVerifier>();
        services.AddSingleton<IEmailSender, Mail.SmtpEmailSender>();
        services.AddSingleton<IApiKeyStore>(provider => new FileSystemApiKeyStore(
            Path.Combine(options.Storage.Directory, "api_keys.json"),
            provider.GetRequiredService<ILogger<FileSystemApiKeyStore>>()));

        AddExtensions(services, options);

        switch (options.Database.ResolveProvider())
        {
            case DatabaseProvider.Sqlite:
                services.AddDbContext<PanelDbContext>(builder =>
                    builder.UseSqlite(options.Database.ConnectionString));
                AddRelationalStores(services);
                break;

            case DatabaseProvider.MySql:
            case DatabaseProvider.MariaDb:
                services.AddDbContext<PanelDbContext>(builder =>
                    builder.UseMySql(
                        options.Database.ConnectionString,
                        MariaDbServerVersion.LatestSupportedServerVersion));
                AddRelationalStores(services);
                break;

            case DatabaseProvider.PostgreSql:
                services.AddDbContext<PanelDbContext>(builder =>
                    builder.UseNpgsql(options.Database.ConnectionString));
                AddRelationalStores(services);
                break;

            case DatabaseProvider.MongoDb:
                AddMongoStores(services, options.Database);
                break;

            case DatabaseProvider.Cassandra:
                AddCassandraStores(services, options.Database);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported database provider '{options.Database.Provider}'.");
        }

        return services;
    }

    private static void AddExtensions(IServiceCollection services, PanelOptions options)
    {
        services.AddSingleton<IThemeCatalog>(provider => new FileSystemThemeCatalog(
            options.Themes.Directory,
            provider.GetRequiredService<ILogger<FileSystemThemeCatalog>>()));

        services.AddSingleton<IAddonState>(provider => new FileSystemAddonState(
            Path.Combine(options.Storage.Directory, "addons.json"),
            provider.GetRequiredService<ILogger<FileSystemAddonState>>()));

        services.AddSingleton<IAddonCatalog>(provider => new FileSystemAddonCatalog(
            options.Addons.Directory,
            provider.GetRequiredService<IAddonState>(),
            provider.GetRequiredService<ILogger<FileSystemAddonCatalog>>()));

        services.AddSingleton<IPanelBrandingStore>(provider => new FileSystemPanelBrandingStore(
            options.Storage.Directory,
            options.App.Name,
            provider.GetRequiredService<ILogger<FileSystemPanelBrandingStore>>()));

        services.AddSingleton<IThemeState>(provider => new FileSystemThemeState(
            Path.Combine(options.Storage.Directory, "theme.json"),
            options.Themes.Active,
            provider.GetRequiredService<IThemeCatalog>(),
            provider.GetRequiredService<ILogger<FileSystemThemeState>>()));

        services.AddSingleton<INestCatalog>(provider => new FileSystemNestCatalog(
            options.Nests.Directory,
            provider.GetRequiredService<ILogger<FileSystemNestCatalog>>()));

        services.AddScoped<IGameTemplateCatalog, GameTemplateCatalog>();

        // The schema is created/migrated in the background; requests are answered
        // with the maintenance page until this singleton reports ready.
        services.AddSingleton<MaintenanceState>();
        services.AddHostedService<DatabaseInitializationService>();

        // The web application also asks the host for admin tabs, addon pages and
        // account guards, so register one shared instance and run it as a service.
        services.AddSingleton<AddonHost>();
        services.AddHostedService(provider => provider.GetRequiredService<AddonHost>());
    }

    private static void AddRelationalStores(IServiceCollection services)
    {
        services.AddScoped<IUserStore, EfUserStore>();
        services.AddScoped<IServerStore, EfServerStore>();
        services.AddScoped<INodeStore, EfNodeStore>();
        services.AddScoped<IEggStore, EfEggStore>();
        services.AddScoped<IAllocationStore, EfAllocationStore>();
        services.AddScoped<IPanelStateStore, EfPanelStateStore>();
        services.AddScoped<IDatabaseInitializer, RelationalDatabaseInitializer>();
    }

    private static void AddMongoStores(IServiceCollection services, DatabaseOptions database)
    {
        services.AddSingleton<IMongoClient>(_ => new MongoClient(database.ConnectionString));
        services.AddSingleton(provider =>
        {
            var client = provider.GetRequiredService<IMongoClient>();
            var url = MongoUrl.Create(database.ConnectionString);
            var name = string.IsNullOrWhiteSpace(database.DatabaseName)
                ? url.DatabaseName ?? "rabbictyl"
                : database.DatabaseName;

            return client.GetDatabase(name);
        });

        services.AddScoped<IUserStore, MongoUserStore>();
        services.AddScoped<IServerStore, MongoServerStore>();
        services.AddScoped<INodeStore, MongoNodeStore>();
        services.AddScoped<IEggStore, MongoEggStore>();
        services.AddScoped<IAllocationStore, MongoAllocationStore>();
        services.AddScoped<IPanelStateStore, MongoPanelStateStore>();
        services.AddScoped<IDatabaseInitializer, MongoDatabaseInitializer>();
    }

    private static void AddCassandraStores(IServiceCollection services, DatabaseOptions database)
    {
        // Validates the keyspace before it is interpolated into CQL.
        _ = CassandraSchema.Statements(database.Keyspace);

        services.AddSingleton(_ => BuildCassandraCluster(database.ConnectionString));
        services.AddSingleton(provider =>
        {
            var cluster = provider.GetRequiredService<Cluster>();
            return cluster.Connect();
        });

        services.AddScoped<IUserStore, CassandraUserStore>();
        services.AddScoped<IServerStore, CassandraServerStore>();
        services.AddScoped<INodeStore, CassandraNodeStore>();
        services.AddScoped<IEggStore, CassandraEggStore>();
        services.AddScoped<IAllocationStore, CassandraAllocationStore>();
        services.AddScoped<IPanelStateStore, CassandraPanelStateStore>();
        services.AddScoped<IDatabaseInitializer, CassandraDatabaseInitializer>();
    }

    private static Cluster BuildCassandraCluster(string contactPoints)
    {
        var builder = Cluster.Builder();

        var points = contactPoints
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (points.Length == 0)
        {
            throw new InvalidOperationException(
                "database.connection_string must list at least one Cassandra contact point.");
        }

        foreach (var point in points)
        {
            var separator = point.LastIndexOf(':');

            if (separator > 0 && int.TryParse(point[(separator + 1)..], out var port))
            {
                builder.AddContactPoint(point[..separator]).WithPort(port);
            }
            else
            {
                builder.AddContactPoint(point);
            }
        }

        return builder.Build();
    }
}
