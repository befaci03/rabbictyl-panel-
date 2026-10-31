namespace Rabbictyl.Core.Entities;

public sealed class AuditLog : Entity
{
    public Guid? ActorId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string? MetadataJson { get; set; }
}
