using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record RequestTabServices(RequestRunner Runner, SecretStore Secrets, RequestLibrary Library, EnvironmentsViewModel Environments, IDialogs Dialogs, Translator Translator, ILogger<RequestTabViewModel> Logger)
{
    public string? ProblemOfName(string name) =>
        !RequestLibrary.IsValidName(name) ? Translator.Of("Save.Invalid")
        : Library.Exists(name) ? Translator.Of("Save.Exists")
        : null;
}
