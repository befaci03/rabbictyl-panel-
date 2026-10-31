using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
