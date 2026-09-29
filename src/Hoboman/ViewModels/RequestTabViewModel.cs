using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class RequestTabViewModel : ObservableObject, IAuthFields
{
    readonly RequestTabServices _services;
    Guid _id;
    bool _ownsId;
    OAuthSettings? _oauth;
    string _savedJson = "";
    string _savedPassword = "";
    string _savedToken = "";
    ProblemMessage? _fileProblem;
    bool _loading;
    CancellationTokenSource? _sending;

    public RequestTabViewModel(RequestTabServices services, ApiRequest request, string? name = null, string? suggestedName = null, string? historyName = null)
    {
        _services = services;
        Name = name;
        SuggestedName = suggestedName;
        HistoryName = historyName;
        _ownsId = historyName is null;
        Query.Changed += MarkDirty;
        Headers.Changed += MarkDirty;
        Send = new AsyncCommand(SendAsync);
        Save = new AsyncCommand(SaveAsync);
        Load(request);
    }

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public string? Name
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Folder));
            }
        }
    }

    public string? SuggestedName
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public Guid Id => _id;

    // A tab owns its id when the file and the secrets under that id are its own. A tab opened from the history borrows the id of the request it ran,
    // so it can send with that request's secrets, but gets its own id before it changes a secret or is saved.
    public bool OwnsId => _ownsId;

    // The history file the tab was opened from, so opening that call again shows this tab.
    public string? HistoryName
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(FromHistory));
            }
        }
    }

    public bool FromHistory => HistoryName is not null;

    public string? Title => (Name ?? SuggestedName) is { } name ? RequestLibrary.LastPartOf(name) : null;

    public string? Folder => RequestLibrary.ParentOf(Name) is { } parent ? $"{parent.Replace("/", " / ")} /" : null;

    public string Method { get; set => Change(ref field, value); } = "GET";

    public string Url { get; set => Change(ref field, value); } = "";

    public KeyValueListViewModel Query { get; } = new();

    public KeyValueListViewModel Headers { get; } = new();

    public BodyKind BodyKind { get; set => Change(ref field, value); }

    public string Body { get; set => Change(ref field, value); } = "";

    public AuthKind AuthKind { get; set => Change(ref field, value); }

    public string UserName { get; set => Change(ref field, value); } = "";

    public string Password { get; set => Change(ref field, value); } = "";

    public string Token { get; set => Change(ref field, value); } = "";

    public bool IsDirty { get; private set => Set(ref field, value); }

    public bool IsSending { get; private set => Set(ref field, value); }

    public ResponseDisplay? Response { get; private set => Set(ref field, value); }

    public ProblemMessage? Problem { get; private set => Set(ref field, value); }

    public AsyncCommand Send { get; }

    public AsyncCommand Save { get; }

    // Changes on disk win over unsaved changes, but the tab's own saves must not reload it.
    public bool ReloadIfChanged(ApiRequest request)
    {
        if (Problem is not null && Problem == _fileProblem)
        {
            Problem = null;
        }
        if (JsonSerializer.Serialize(request) == _savedJson)
        {
            // A tab that got its file back is only unsaved if it was edited.
            IsDirty = HasUnsavedChanges();
            return false;
        }
        Load(request);
        return true;
    }

    public void ShowFileProblem(string details) => Problem = _fileProblem = new(_services.Translator.Of("Open.Failed"), details);

    public async Task ShowAsync(HistoryEntry entry)
    {
        Response = entry.Response is { } response ? await Task.Run(() => ResponseDisplay.Of(response)) : null;
        Problem = entry.Error is { } error ? new(_services.Translator.Of("Response.Failed"), error) : null;
    }

    public async Task LoadSecretsAsync(CancellationToken cancellationToken)
    {
        string? password = null;
        string? token = null;
        if (_id == Guid.Empty)
        {
            _services.Logger.LogInformation("{Name} has no id, so it has no saved secrets", Name);
        }
        else
        {
            try
            {
                password = await _services.Secrets.OfAsync(_id, SecretKind.Password, cancellationToken);
                token = await _services.Secrets.OfAsync(_id, SecretKind.Token, cancellationToken);
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                _services.Logger.LogError(exception, "Could not load the secrets for {Name}", Name);
                Problem = new(_services.Translator.Of("Response.SecretsFailed"), exception.Message);
            }
        }
        _loading = true;
        Password = _savedPassword = password ?? "";
        Token = _savedToken = token ?? "";
        _loading = false;
    }

    public async Task SaveAsync()
    {
        var translator = _services.Translator;
        var name = Name ?? _services.Dialogs.AskName(translator.Of("Save.Title"), SuggestedName ?? "", translator.Of("Common.Save"), _services.ProblemOfName);
        if (name is null)
        {
            return;
        }
        EnsureOwnId();
        try
        {
            // The secrets go first, so a failure leaves no request file behind that points at secrets that were never saved.
            await SaveSecretsAsync(CancellationToken.None);
            var request = ToRequest();
            await _services.Library.SaveAsync(name, request, CancellationToken.None);
            _savedJson = JsonSerializer.Serialize(request);
            Name = name;
            // Edits made while the file was being written are still unsaved.
            IsDirty = HasUnsavedChanges();
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save {Name}", name);
            Problem = new(translator.Of("Save.Failed"), exception.Message);
        }
    }

    public void Cancel() => _sending?.Cancel();

    public void Rename(string name) => Name = name;

    public void Unlink()
    {
        SuggestedName = Name;
        Name = null;
        IsDirty = true;
    }

    // Once the secrets are deleted, a later save writes the ones shown again.
    public void ForgetSavedSecrets() => _savedPassword = _savedToken = "";

    public ApiRequest ToRequest() => new()
    {
        Id = _id,
        Method = Method,
        Url = Url,
        Query = Query.ToList(),
        Headers = Headers.ToList(),
        BodyKind = BodyKind,
        Body = Body,
        Auth = new(AuthKind, UserName, _oauth),
    };

    public async Task SendAsync()
    {
        Problem = null;
        Response = null;
        IsSending = true;
        using var sending = _sending = new CancellationTokenSource();
        try
        {
            await SaveSecretsAsync(sending.Token);
            var response = await _services.Runner.RunAsync(ToRequest(), Name ?? SuggestedName, _services.Environments.Selected, HistorySource.App, sending.Token);
            // Formatting a large body takes a while, so it is kept off the UI thread.
            Response = await Task.Run(() => ResponseDisplay.Of(response));
        }
        catch (OperationCanceledException) when (sending.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _services.Logger.LogWarning(exception, "Could not send {Name}", Name ?? SuggestedName);
            Problem = new(_services.Translator.Of("Response.Failed"), ProblemOf(exception));
        }
        finally
        {
            _sending = null;
            IsSending = false;
        }
    }

    string ProblemOf(Exception exception)
    {
        var translator = _services.Translator;
        // A file problem already names the file, which the inner exception leaves out.
        var cause = FileProblem.Is(exception) ? exception.Message : exception.GetBaseException().Message;
        return exception switch
        {
            MissingSecretException { Kind: SecretKind.Password } => translator.Of("Response.MissingPassword"),
            MissingSecretException { Kind: SecretKind.Token } => translator.Of("Response.MissingToken"),
            InvalidHeaderException header => translator.Format("Response.InvalidHeader", header.Name),
            InvalidMethodException method => translator.Format("Response.InvalidMethod", method.Method),
            UriFormatException => WithCause(translator.Of("Response.InvalidUrl")),
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => WithCause(translator.Of("Response.UnknownHost")),
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => WithCause(translator.Of("Response.NoConnection")),
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => WithCause(translator.Of("Response.SecureConnection")),
            TaskCanceledException { InnerException: TimeoutException } => WithCause(translator.Of("Response.Timeout")),
            _ => cause,
        };

        string WithCause(string reason) => $"{reason}{Environment.NewLine}{cause}";
    }

    // Each secret is saved on its own and only marked as saved afterwards, so a value typed while saving is saved next time.
    async Task SaveSecretsAsync(CancellationToken cancellationToken)
    {
        if (Password == _savedPassword && Token == _savedToken)
        {
            return;
        }
        EnsureOwnId();
        var (password, token) = (Password, Token);
        if (password != _savedPassword)
        {
            await _services.Secrets.SaveAsync(_id, SecretKind.Password, password, cancellationToken);
            _savedPassword = password;
        }
        if (token != _savedToken)
        {
            await _services.Secrets.SaveAsync(_id, SecretKind.Token, token, cancellationToken);
            _savedToken = token;
        }
    }

    void EnsureOwnId()
    {
        if (_ownsId && _id != Guid.Empty)
        {
            return;
        }
        _id = Guid.NewGuid();
        _ownsId = true;
        // A new id has no secrets yet, so the ones shown are saved under it.
        _savedPassword = _savedToken = "";
        MarkDirty();
    }

    bool HasUnsavedChanges() => JsonSerializer.Serialize(ToRequest()) != _savedJson || Password != _savedPassword || Token != _savedToken;

    void Load(ApiRequest request)
    {
        _loading = true;
        _savedJson = JsonSerializer.Serialize(request);
        _id = request.Id;
        _oauth = request.Auth.OAuth;
        Method = request.Method;
        Url = request.Url;
        Query.Load(request.Query);
        Headers.Load(request.Headers);
        BodyKind = request.BodyKind;
        Body = request.Body;
        AuthKind = request.Auth.Kind;
        UserName = request.Auth.UserName;
        _loading = false;
        IsDirty = false;
    }

    bool Change<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (!Set(ref storage, value, name))
        {
            return false;
        }
        MarkDirty();
        return true;
    }

    void MarkDirty()
    {
        if (_loading)
        {
            return;
        }
        IsDirty = true;
        // A changed call is a new request, and the history keeps the call as it was.
        HistoryName = null;
    }
}
