using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Workflows;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record WorkflowServices(WorkflowLibrary Library, WorkflowCheck Check, WorkflowRunner Runner, RequestLibrary Requests, EnvironmentsViewModel Environments, AuthRefreshService AuthRefresh, SecretStore Secrets, IDialogs Dialogs, Translator Translator, ILogger<WorkflowViewModel> Logger);
