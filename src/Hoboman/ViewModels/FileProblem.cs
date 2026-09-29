using System.IO;

namespace Hoboman.ViewModels;

static class FileProblem
{
    public static bool Is(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidDataException;
}
