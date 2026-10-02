namespace Hoboman.Services;

public sealed class CollectionChanges
{
    readonly SemaphoreSlim _changes = new(1, 1);

    public bool IsRunning => _changes.CurrentCount == 0;

    public async Task RunAsync(Func<Task> change)
    {
        await _changes.WaitAsync();
        try
        {
            await change();
        }
        finally
        {
            _changes.Release();
        }
    }
}
