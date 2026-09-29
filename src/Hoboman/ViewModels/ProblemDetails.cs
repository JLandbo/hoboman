using Hoboman.Core.Languages;
using Hoboman.Core.Storage;

namespace Hoboman.ViewModels;

public static class ProblemDetails
{
    extension(Translator translator)
    {
        // Hoboman's own message about a broken file is translated; the technical cause below it comes from .NET.
        public string DetailsOf(Exception exception) => exception is InvalidFileException invalid
            ? $"{translator.Format("File.Invalid", invalid.FilePath)}{Environment.NewLine}{invalid.InnerException?.Message}"
            : exception.Message;
    }
}
