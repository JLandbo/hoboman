namespace Hoboman.Tests.Cli;

// Input that never ends, as when nothing is typed, until the read is cancelled.
sealed class PendingReader : TextReader
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task<string> ReadToEndAsync(CancellationToken cancellationToken)
    {
        Started.SetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return "";
    }
}
