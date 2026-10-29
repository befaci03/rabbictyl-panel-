namespace RabbiCtyl.Core.Abstractions;

/// <summary>Creates the schema for the configured database provider.</summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
