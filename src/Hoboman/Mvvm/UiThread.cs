namespace Hoboman.Mvvm;

// The timers of TimeProvider fire off the UI thread, so what they change that is shown goes back to the thread the view model was made on.
// Without such a thread, as in the tests, the work is done at once.
public sealed class UiThread
{
    readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Post(Action work)
    {
        if (_context is null)
        {
            work();
            return;
        }
        _context.Post(_ => work(), null);
    }
}
