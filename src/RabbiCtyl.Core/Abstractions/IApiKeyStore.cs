using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

/// <summary>Application API keys used by bots and external tooling.</summary>
public interface IApiKeyStore
{
    Task<IReadOnlyList<ApiKey>> ListAsync(CancellationToken cancellationToken = default);

    Task<ApiKey?> FindAsync(string identifier, CancellationToken cancellationToken = default);

    Task AddAsync(ApiKey key, CancellationToken cancellationToken = default);

    Task DeleteAsync(ApiKey key, CancellationToken cancellationToken = default);
}
