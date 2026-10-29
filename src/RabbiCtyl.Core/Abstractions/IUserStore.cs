using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Core.Abstractions;

public interface IUserStore
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Matches either the email address or the username, case-insensitively.</summary>
    Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
