using System.Text.Json.Serialization;

namespace RabbiCtyl.Core.Entities;

public sealed class Allocation : Entity
{
    public Guid NodeId { get; set; }

    [JsonIgnore]
    public Node? Node { get; set; }

    public string Ip { get; set; } = "0.0.0.0";

    public int Port { get; set; }

    public Guid? ServerId { get; set; }

    [JsonIgnore]
    public Server? Server { get; set; }
}
