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

public sealed class RequestTabViewModel : ObservableObject
{
    readonly RequestTabServices _services;
    string _savedJson = "";
    ProblemMessage? _fileProblem;
    ApiResponse? _response;
    BodyFormat _bodyFormat;
    bool _loading;
    bool _pinned;
    CancellationTokenSource? _sending;

    public RequestTabViewModel(RequestTabServices services, ApiRequest request, string? name = null, string? suggestedName = null, string? historyName = null)
    {
        _services = services;
        Name = name;
        SuggestedName = suggestedName;
        HistoryName = historyName;
        OwnsId = historyName is null;
        Auth = new(services.Secrets, services.OAuth, services.Environments, services.Translator, services.Clock, services.Logger);
        Auth.Changed += MarkDirty;
        // A login open in the browser belongs to this tab, so the next call from the history must not take its place.
        Auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.IsFetching) && Auth.IsFetching)
            {
                Pin();
            }
        };
        Query.Changed += MarkDirty;
        Headers.Changed += MarkDirty;
        // Without an address there is nothing to send, and trying would only leave a failed call in the history.
        Send = new AsyncCommand(SendAsync, () => !string.IsNullOrWhiteSpace(Url));
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

    public Guid Id { get; private set; }

    // A tab owns its id when the file and the secrets under that id are its own. A tab opened from the history borrows the id of the request it ran,
    // so it can send with that request's secrets, but gets its own id before it changes a secret or is saved.
    public bool OwnsId { get; private set; }

    // The history file the tab was opened from, so opening that call again shows this tab.
    public string? HistoryName
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(FromHistory));
                OnPropertyChanged(nameof(IsPreview));
            }
        }
    }

    public bool FromHistory => HistoryName is not null;

    // A history tab that is only looked at gives its place to the next call opened from the history, unless it is pinned.
    public bool IsPreview => FromHistory && !_pinned;

    // Tabs without a name are numbered, so they can be told apart.
    public int Number { get; init; }

    public string Title => (Name ?? SuggestedName) is { } name ? RequestLibrary.LastPartOf(name) : _services.Translator.Format("Tab.New", Number);

    public string? Folder => RequestLibrary.ParentOf(Name) is { } parent ? $"{parent.Replace("/", " / ")} /" : null;

    public string Method { get; set => Change(ref field, value); } = "GET";

    public string Url
    {
        get;
        set
        {
            Change(ref field, value);
            Send.RaiseCanExecuteChanged();
        }
    } = "";

    public KeyValueListViewModel Query { get; } = new();

    public KeyValueListViewModel Headers { get; } = new();

    public BodyKind BodyKind { get; set => Change(ref field, value); }

    public string Body { get; set => Change(ref field, value); } = "";

    public AuthViewModel Auth { get; }

    public bool IsDirty { get; private set => Set(ref field, value); }

    public bool IsSending { get; private set => Set(ref field, value); }

    public ResponseDisplay? Response { get; private set => Set(ref field, value); }

    // Chosen from the Content-Type of each new response, and changed by the user when it does not fit.
    public BodyFormat BodyFormat
    {
        get => _bodyFormat;
        set
        {
            if (Set(ref _bodyFormat, value) && _response is { } response)
            {
                Formatting = FormatAsync(response);
            }
        }
    }

    internal Task Formatting { get; private set; } = Task.CompletedTask;

    public ProblemMessage? Problem { get; private set => Set(ref field, value); }

    // All tabs share one view, so each tab keeps which sections it shows.
    public RequestSection RequestSection { get; set => Set(ref field, value); }

    public ResponseSection ResponseSection { get; set => Set(ref field, value); }

    public AsyncCommand Send { get; }

    public AsyncCommand Save { get; }

    // Changes on disk win over unsaved changes, but the tab's own saves must not reload it.
    public bool ReloadIfChanged(ApiRequest request)
    {
        if (Problem == _fileProblem)
        {
            Problem = null;
        }
        if (SavedJsonOf(request) == _savedJson)
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
        await ShowAsync(entry.Response);
        Problem = entry.Error is { } error ? new(_services.Translator.Of("Response.Failed"), error) : null;
    }

    public async Task LoadSecretsAsync(CancellationToken cancellationToken)
    {
        if (Id == Guid.Empty)
        {
            _services.Logger.LogInformation("{Name} has no id, so it has no saved secrets", Name);
        }
        try
        {
            await Auth.LoadSecretsAsync(Id, cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not load the secrets for {Name}", Name);
            Problem = new(_services.Translator.Of("Response.SecretsFailed"), _services.Translator.DetailsOf(exception));
        }
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
            _savedJson = SavedJsonOf(request);
            Name = name;
            // Edits made while the file was being written are still unsaved.
            IsDirty = HasUnsavedChanges();
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save {Name}", name);
            Problem = new(translator.Of("Save.Failed"), translator.DetailsOf(exception));
        }
    }

    public void Cancel() => _sending?.Cancel();

    public void Pin()
    {
        _pinned = true;
        OnPropertyChanged(nameof(IsPreview));
    }

    async Task ShowAsync(ApiResponse? response)
    {
        _response = response;
        Response = null;
        if (response is not null)
        {
            Set(ref _bodyFormat, ResponseDisplay.FormatOf(response), nameof(BodyFormat));
            await FormatAsync(response);
        }
    }

    // Formatting a large body takes a while, so it is kept off the UI thread, and a newer response or format wins.
    async Task FormatAsync(ApiResponse response)
    {
        var format = _bodyFormat;
        var display = await Task.Run(() => ResponseDisplay.Of(response, format));
        if (ReferenceEquals(response, _response) && format == _bodyFormat)
        {
            Response = display;
        }
    }

    public void Rename(string name) => Name = name;

    public void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        Auth.Relabel();
    }

    public void Unlink()
    {
        SuggestedName = Name;
        Name = null;
        IsDirty = true;
    }

    ApiRequest ToRequest() => new()
    {
        Id = Id,
        Method = Method,
        Url = Url,
        Query = Query.ToList(),
        Headers = Headers.ToList(),
        BodyKind = BodyKind,
        Body = Body,
        Auth = Auth.ToSettings(),
    };

    public async Task SendAsync()
    {
        // Sending makes a new call, so the history entry opens the old one again.
        HistoryName = null;
        Problem = null;
        _response = null;
        Response = null;
        IsSending = true;
        // The environment chosen when Send is pressed is the one used, even if another is chosen while the secrets are saved.
        var environment = _services.Environments.Selected;
        using var sending = _sending = new CancellationTokenSource();
        try
        {
            await SaveSecretsAsync(sending.Token);
            // Secrets were all that was unsaved if the request itself is unchanged, such as after fetching a token.
            IsDirty = HasUnsavedChanges();
            var response = await _services.Runner.RunAsync(ToRequest(), Name ?? SuggestedName, environment, HistorySource.App, sending.Token);
            await ShowAsync(response);
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
        // A file problem names the file, which the inner exception leaves out.
        var cause = FileProblem.Is(exception) ? translator.DetailsOf(exception) : exception.GetBaseException().Message;
        return exception switch
        {
            MissingSecretException { Kind: SecretKind.Password } => translator.Of("Response.MissingPassword"),
            MissingSecretException { Kind: SecretKind.Token } => translator.Of("Response.MissingToken"),
            MissingSecretException { Kind: SecretKind.OAuthToken } => translator.Of("Response.MissingOAuthToken"),
            ExpiredTokenException expired => translator.Format("Response.ExpiredToken", expired.ExpiresAt.ToLocalTime()),
            InvalidHeaderException header => translator.Format("Response.InvalidHeader", header.Name),
            InvalidMethodException method => translator.Format("Response.InvalidMethod", method.Method),
            _ => NetworkProblem.Of(exception, translator) ?? cause,
        };
    }

    async Task SaveSecretsAsync(CancellationToken cancellationToken)
    {
        if (!Auth.HasUnsavedSecrets)
        {
            return;
        }
        if (Name is { } name && OwnsId && await _services.Library.SharesRequestIdAsync(name, Id, cancellationToken))
        {
            _services.Logger.LogWarning("{Name} shares its id with another request, so it gets its own", name);
            TakeNewId();
        }
        EnsureOwnId();
        await Auth.SaveSecretsAsync(Id, cancellationToken);
    }

    void EnsureOwnId()
    {
        if (!OwnsId || Id == Guid.Empty)
        {
            TakeNewId();
        }
    }

    void TakeNewId()
    {
        Id = Guid.NewGuid();
        OwnsId = true;
        // A new id has no secrets yet, so the ones shown are saved under it.
        Auth.ForgetSavedSecrets();
        MarkDirty();
    }

    bool HasUnsavedChanges() => SavedJsonOf(ToRequest()) != _savedJson || Auth.HasUnsavedSecrets;

    // The lists leave blank rows out, so a file with blank rows is compared the same way.
    static string SavedJsonOf(ApiRequest request) =>
        JsonSerializer.Serialize(request with { Query = KeyValueListViewModel.WithoutBlanks(request.Query), Headers = KeyValueListViewModel.WithoutBlanks(request.Headers) });

    void Load(ApiRequest request)
    {
        _loading = true;
        _savedJson = SavedJsonOf(request);
        Id = request.Id;
        Method = request.Method;
        Url = request.Url;
        Query.Load(request.Query);
        Headers.Load(request.Headers);
        BodyKind = request.BodyKind;
        Body = request.Body;
        Auth.Load(request.Auth);
        _loading = false;
        IsDirty = false;
    }

    void Change<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (Set(ref storage, value, name))
        {
            MarkDirty();
        }
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
