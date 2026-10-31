namespace Rabbictyl.Core.Entities;

/// <summary>
/// Base type for every persisted aggregate. Ids are UUIDv7 so they stay
/// time-ordered (good for B-tree indexes) while remaining globally unique.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
