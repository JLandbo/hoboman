namespace Hoboman.Mvvm;

// Runs the work one at a time. Asking again while it runs makes it run once more afterwards, so reloads never overlap or come back out of order.
public sealed class Coalescer(Func<Task> work)
{
    bool _running;
    bool _again;

    public async Task RunAsync()
    {
        if (_running)
        {
            _again = true;
            return;
        }
        _running = true;
        try
        {
            do
            {
                _again = false;
                await work();
            }
            while (_again);
        }
        finally
        {
            _running = false;
        }
    }
}
