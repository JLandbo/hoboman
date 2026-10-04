using Microsoft.Extensions.Time.Testing;

namespace Hoboman.Tests.Workflows;

// Tells when something starts waiting on it, so a test moves the time only once the wait has begun.
sealed class WatchedClock : FakeTimeProvider
{
    public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Waiting.TrySetResult();
        return timer;
    }
}
