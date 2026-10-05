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

    // Only a plain path on a drive is taken. \\?\, \\.\, a share or an NTFS stream such as ::$INDEX_ALLOCATION would lead past the check of the folder,
    // and a share would make Windows log in to another machine.
    public string Outside(string path)
    {
        var full = Path.GetFullPath(path);
        if (full is not [var drive, ':', '\\', ..] || !char.IsAsciiLetter(drive) || full.IndexOf(':', 2) >= 0)
        {
            throw new OwnFileException("Only a plain path on a drive, such as C:\\folder\\file, can be used.");
        }
        return folder.Holds(full) ? throw new OwnFileException("Hoboman's own files cannot be used.") : full;
    }
}

sealed class OwnFileException(string message) : Exception(message);
