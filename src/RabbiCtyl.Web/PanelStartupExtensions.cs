using Microsoft.Extensions.DependencyInjection;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Web;

public static class PanelStartupExtensions
{
    public static async Task InitializePanelAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(cancellationToken);

        await scope.ServiceProvider
            .GetRequiredService<IAdminSeeder>()
            .SeedAsync(cancellationToken);
    }
}
