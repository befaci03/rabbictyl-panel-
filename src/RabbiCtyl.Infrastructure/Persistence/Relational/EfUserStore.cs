using Microsoft.EntityFrameworkCore;
using RabbiCtyl.Core.Abstractions;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Infrastructure.Persistence.Relational;

public sealed class EfUserStore : IUserStore
{
    private readonly PanelDbContext _db;

    public EfUserStore(PanelDbContext db)
    {
        _db = db;
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        var normalized = login.Trim().ToLowerInvariant();

        return _db.Users.FirstOrDefaultAsync(
            user => user.Email.ToLower() == normalized || user.Username.ToLower() == normalized,
            cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Users
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _db.Users.CountAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Users.Update(user);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
