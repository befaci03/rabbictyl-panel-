namespace Rabbictyl.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
