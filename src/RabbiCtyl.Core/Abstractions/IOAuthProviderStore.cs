using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

public interface IOAuthProviderStore
{
    Task<IReadOnlyList<OAuthProvider>> ListAsync(CancellationToken cancellationToken = default);

    Task<OAuthProvider?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(OAuthProvider provider, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
