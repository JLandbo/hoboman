using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record RequestTabServices(RequestRunner Runner, SecretStore Secrets, AuthRefreshService AuthRefresh, RequestLibrary Library, CollectionChanges CollectionChanges, EnvironmentsViewModel Environments, IDialogs Dialogs, Translator Translator, TimeProvider Clock, ILogger<RequestTabViewModel> Logger)
{
    // A name typed in a dialog is one part of a path, so it cannot hold a '/'.
    public Func<string, string?> OnePart(Func<string, string?> problemOf) => name => name.Contains('/') ? Translator.Of("Save.Invalid") : problemOf(name);

    public string? ProblemOfName(string name) =>
        !RequestLibrary.IsValidName(name) ? Translator.Of("Save.Invalid")
        : Library.Exists(name) ? Translator.Of("Save.Exists")
        : null;
}
