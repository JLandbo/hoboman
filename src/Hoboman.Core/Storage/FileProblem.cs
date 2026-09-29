namespace Hoboman.Core.Storage;

public static class FileProblem
{
    public static bool Is(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidDataException;
}
