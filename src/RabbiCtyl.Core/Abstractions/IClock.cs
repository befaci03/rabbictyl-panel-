namespace RabbiCtyl.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
