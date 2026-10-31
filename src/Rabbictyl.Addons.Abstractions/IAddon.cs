namespace Rabbictyl.Addons.Abstractions;

/// <summary>
/// C# entry point for an addon. Implementations are discovered from the
/// assembly listed in <c>manifest.json</c> (the <c>entry</c> field).
/// </summary>
public interface IAddon
{
    string Id { get; }

    Task InitializeAsync(AddonContext context, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
