using Cassandra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Infrastructure.Extensions;
using RabbiCtyl.Infrastructure.Pats;
using RabbiCtyl.Infrastructure.Persistence.Cassandra;
using RabbiCtyl.Infrastructure.Persistence.Mongo;
using RabbiCtyl.Infrastructure.Persistence.Relational;
using RabbiCtyl.Infrastructure.Security;

namespace RabbiCtyl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRabbiCtylInfrastructure(
        this IServiceCollection services,
        PanelOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(options.Database);
        services.AddSingleton(options.Security);
        services.AddSingleton(options.Bootstrap);
        services.AddMemoryCache();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher>(_ => new BcryptPasswordHasher(options.Security.BcryptWorkFactor));
        services.AddSingleton<IAdminSeeder, AdminSeeder>();

        services.AddHttpClient("pats", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IPatsClientFactory, PatsClientFactory>();

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

        services.AddSingleton<IAddonCatalog>(provider => new FileSystemAddonCatalog(
            options.Addons.Directory,
            provider.GetRequiredService<ILogger<FileSystemAddonCatalog>>()));

        services.AddSingleton<IThemeState>(provider => new FileSystemThemeState(
            Path.Combine(options.Storage.Directory, "theme.json"),
            options.Themes.Active,
            provider.GetRequiredService<IThemeCatalog>(),
            provider.GetRequiredService<ILogger<FileSystemThemeState>>()));

        services.AddHostedService<AddonHost>();
    }

    private static void AddRelationalStores(IServiceCollection services)
    {
        services.AddScoped<IUserStore, EfUserStore>();
        services.AddScoped<IServerStore, EfServerStore>();
        services.AddScoped<INodeStore, EfNodeStore>();
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
