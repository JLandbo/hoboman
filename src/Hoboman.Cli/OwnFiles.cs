using System.Text;
using Hoboman.Core.Storage;

namespace Hoboman.Cli;

// Hoboman's own files are only reached through its commands, so a path in its folder is refused, both to read and to write.
sealed class OwnFiles(AppFolder folder)
{
    public Task<string> ReadAsync(string path, CancellationToken cancellationToken) => File.ReadAllTextAsync(Outside(path), Encoding.UTF8, cancellationToken);

    // A file that is there already is never overwritten, so --out cannot destroy one, not even if it comes after the check.
    public static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await file.WriteAsync(bytes, cancellationToken);
    }

    public string Outside(string path) => folder.Holds(path) ? throw new OwnFileException() : Path.GetFullPath(path);
}

sealed class OwnFileException() : Exception("Hoboman's own files cannot be used.");
