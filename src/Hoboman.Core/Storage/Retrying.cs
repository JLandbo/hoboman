using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Storage;

// Another program, such as a virus scanner looking at a file that was just written, can hold a file for a moment, so the work is tried a few times before it fails.
static class Retrying
{
    public const int Attempts = 5;

    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(50);

    public static async Task RunAsync(Action work, ILogger logger, string name, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await Task.Run(work, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (FileProblem.Is(exception) && attempt < Attempts)
            {
                logger.LogDebug(exception, "{Name} could not be used, trying again", name);
                await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
