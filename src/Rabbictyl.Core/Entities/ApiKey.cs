using System.Text.Json.Serialization;

namespace Rabbictyl.Core.Entities;

public sealed class ApiKey : Entity
{
    public Guid UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

    public string Identifier { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string? Memo { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
