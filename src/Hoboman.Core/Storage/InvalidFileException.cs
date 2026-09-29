namespace Hoboman.Core.Storage;

public sealed class InvalidFileException(string filePath, Exception inner) : IOException($"{filePath} is not valid: {inner.Message}", inner)
{
    public string FilePath => filePath;
}
