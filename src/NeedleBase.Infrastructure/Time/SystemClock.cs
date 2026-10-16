using NeedleBase.Application.Abstractions;

namespace NeedleBase.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
