using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
