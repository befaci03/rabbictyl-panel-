using System.Text.Json.Serialization;

namespace RabbiCtyl.Core.Entities;

public sealed class User : Entity
{
    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NameFirst { get; set; } = string.Empty;

    public string NameLast { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsRootAdmin { get; set; }

    public bool UseTotp { get; set; }

    public string? TotpSecret { get; set; }

    public string Language { get; set; } = "en";

    [JsonIgnore]
    public ICollection<Server> Servers { get; set; } = new List<Server>();

    public string DisplayName =>
        string.Join(' ', new[] { NameFirst, NameLast }.Where(part => !string.IsNullOrWhiteSpace(part)))
            is { Length: > 0 } name
            ? name
            : Username;
}
