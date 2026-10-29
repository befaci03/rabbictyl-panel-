namespace RabbiCtyl.Core.Abstractions;

/// <summary>Creates the initial administrator account on an empty installation.</summary>
public interface IAdminSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
