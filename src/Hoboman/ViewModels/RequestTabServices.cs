using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record RequestTabServices(RequestRunner Runner, SecretStore Secrets, AuthRefreshService AuthRefresh, RequestLibrary Library, RequestSnapshot Requests, CollectionChanges CollectionChanges, EnvironmentsViewModel Environments, CredentialsViewModel Credentials, IDialogs Dialogs, Translator Translator, TimeProvider Clock, ILogger<RequestTabViewModel> Logger)
{
    // Names need not be unique, like in Postman, as requests and folders are known by their ids.
    public string? ProblemOfName(string name) => RequestLibrary.IsValidName(name) ? null : Translator.Of("Save.Invalid");
}
