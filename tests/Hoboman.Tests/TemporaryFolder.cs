namespace Hoboman.Tests;

public sealed class TemporaryFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());

    // A watcher or the app can hold a file for a moment after a test, so the folder is deleted again a few times before giving up.
    public void Dispose()
    {
        for (var attempt = 1; Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (attempt < 10 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
