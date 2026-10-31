using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Infrastructure.Persistence.Mongo;

/// <summary>
/// The domain entities are persistence agnostic. MongoDB needs the navigation
/// properties removed (they would create cycles) and a stable Guid encoding.
/// </summary>
internal static class MongoMappings
{
    private static int _initialized;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        Register<User>(map => map.UnmapProperty(user => user.Servers));
        Register<Server>(map =>
        {
            map.UnmapProperty(server => server.Owner);
            map.UnmapProperty(server => server.Node);
            map.UnmapProperty(server => server.Egg);
        });
        Register<Node>(map =>
        {
            map.UnmapProperty(node => node.Allocations);
            map.UnmapProperty(node => node.Servers);
        });
        Register<Allocation>(map =>
        {
            map.UnmapProperty(allocation => allocation.Node);
            map.UnmapProperty(allocation => allocation.Server);
        });
        Register<ApiKey>(map => map.UnmapProperty(apiKey => apiKey.User));
        Register<Egg>(map => map.UnmapProperty(egg => egg.Variables));
        Register<AuditLog>(_ => { });
        Register<PanelState>(_ => { });
    }

    private static void Register<TEntity>(Action<BsonClassMap<TEntity>> configure)
        where TEntity : class
    {
        BsonClassMap.RegisterClassMap<TEntity>(map =>
        {
            map.AutoMap();
            configure(map);

            map.IdMemberMap?.SetSerializer(new GuidSerializer(GuidRepresentation.Standard));
        });
    }
}
