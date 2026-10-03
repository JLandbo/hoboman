using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Base64;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Hoboman.Services;
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
    CancellationTokenSource? _formatting;
    LayoutProblem? _layoutProblem;
    bool _layingOut;
    AuthSource? _inheritedAuth;
    CancellationTokenSource? _refreshingAuth;
    bool _closed;
    int _authResolution;

    public RequestTabViewModel(RequestTabServices services, ApiRequest request, string? name = null, string? suggestedName = null, string? historyName = null, string? destination = null)
    {
        _services = services;
        Name = name;
        SuggestedName = suggestedName;
        HistoryName = historyName;
        IsDraft = destination is not null;
        Destination = destination;
        OwnsId = historyName is null;
        Auth = new(services.Secrets, services.AuthRefresh, services.Environments, services.Translator, services.Clock, services.Logger);
        Auth.Changed += MarkDirty;
        // A login open in the browser belongs to this tab, so the next call from the history must not take its place.
        Auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.Kind) && !_loading)
            {
                _ = UpdateAuthSourceAsync();
            }
            if (e.PropertyName is nameof(AuthViewModel.Kind) or nameof(AuthViewModel.IsFetching))
            {
                RefreshAuthHeader();
            }
            if (e.PropertyName == nameof(AuthViewModel.IsFetching) && Auth.IsFetching)
            {
                Pin();
            }
        };
        services.AuthRefresh.Changed += RefreshAuthHeader;
        Query.Changed += MarkDirty;
        Headers.Changed += MarkDirty;
        Base64 = new(services.Translator, services.Clock);
        Base64.Changed += MarkDirty;
        Base64.DecodeChanged += ShowResponseAgain;
        // Without an address there is nothing to send, and trying would only leave a failed call in the history.
        Send = new AsyncCommand(SendAsync, () => !string.IsNullOrWhiteSpace(Url) && !IsAuthRefreshing);
        Save = new AsyncCommand(SaveAsync);
        Load(request);
    }

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public event Func<RequestTabViewModel, Task>? Created;

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

    public string? Folder => RequestLibrary.ParentOf(Name ?? DraftName) is { } parent ? $"{parent.Replace("/", " / ")} /" : null;

    public bool IsDraft
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(IsUnsaved));
                OnPropertyChanged(nameof(Folder));
            }
        }
    }

    public string? Destination
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Folder));
            }
        }
    }

    public string? DraftName => !IsDraft ? null : Destination is { } folder ? $"{folder}/{Title}" : Title;

    public bool IsUnsaved => IsDraft || IsDirty;

    public void MoveTo(string? destination)
    {
        Destination = destination;
        _ = UpdateAuthSourceAsync();
    }

    public string Method { get; set => Change(ref field, value); } = "GET";

    internal string SavedMethod { get; private set; } = "GET";

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

    public BodyKind BodyKind
    {
        get;
        set
        {
            Change(ref field, value);
            ShowLayoutProblem(null);
        }
    }

    public string Body
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                MarkDirty();
                ShowLayoutProblem(null);
                Base64.BodyChanged(value, UseEnvironmentVariablesInBody);
            }
        }
    } = "";

    public bool UseEnvironmentVariablesInBody
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                MarkDirty();
                ShowLayoutProblem(null);
                Base64.BodyChanged(Body, value);
            }
        }
    }

    // Why the body could not be laid out, until it or its kind changes, in the language of the moment.
    public string? BodyLayoutProblem => _layoutProblem switch
    {
        LayoutProblem.NotJson => _services.Translator.Of("Body.NotJson"),
        LayoutProblem.NotXml => _services.Translator.Of("Body.NotXml"),
        LayoutProblem.NeedsVariables => _services.Translator.Of("Body.NeedsVariables"),
        _ => null,
    };

    public Base64ViewModel Base64 { get; }

    public AuthViewModel Auth { get; }

    AuthKind? EffectiveAuthKind => Auth.Kind == AuthKind.Inherit ? _inheritedAuth?.Settings.Kind : Auth.Kind;

    public string? InheritedAuthFolder => Auth.Kind == AuthKind.Inherit ? _inheritedAuth?.Folder : null;

    public bool HasInheritedAuth => InheritedAuthFolder is not null;

    public string AuthHeader => $"{_services.Translator.Of("Editor.Auth")} ({AuthTypeLabel})";

    string AuthTypeLabel => EffectiveAuthKind switch
    {
        AuthKind.None => _services.Translator.Of("Auth.None"),
        AuthKind.Basic => _services.Translator.Of("Auth.Basic"),
        AuthKind.Bearer => _services.Translator.Of("Auth.BearerShort"),
        AuthKind.OAuth2 => "OAuth",
        _ => "…",
    };

    public string? AuthSourceTip => InheritedAuthFolder is { } folder ? _services.Translator.Format("Auth.InheritedFrom", folder.Replace("/", " / ")) : null;

    public string RefreshAuthTip => _services.Translator.Format("OAuth.Reauthenticate", InheritedAuthFolder ?? Title, _services.Environments.Selected?.Name ?? _services.Translator.Of("Environment.None"));

    public bool HasOAuth => EffectiveAuthKind == AuthKind.OAuth2;

    string AuthOwner => AuthRefreshService.OwnerOf(Auth.Kind == AuthKind.Inherit && _inheritedAuth is { } inherited ? inherited : new(Id, Auth.ToSettings()));

    public bool IsAuthRefreshing => _refreshingAuth is not null || Auth.IsFetching || _services.AuthRefresh.IsRefreshing(AuthOwner, EnvironmentOrNone().Id);

    public bool CanRefreshAuth => !_closed && HasOAuth && !IsAuthRefreshing;

    public async Task UpdateAuthSourceAsync()
    {
        var resolution = ++_authResolution;
        var name = Name ?? SuggestedName ?? DraftName;
        var request = ToRequest();
        try
        {
            var source = await _services.Library.AuthOfAsync(name, request, CancellationToken.None);
            if (resolution == _authResolution)
            {
                _inheritedAuth = source;
                RefreshAuthHeader();
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            if (resolution == _authResolution)
            {
                _inheritedAuth = null;
                RefreshAuthHeader();
            }
            _services.Logger.LogWarning(exception, "Could not resolve the auth of {Name}", name);
        }
    }

    public async Task<bool> RefreshAuthAsync()
    {
        if (_closed || IsAuthRefreshing)
        {
            return false;
        }
        var environment = EnvironmentOrNone();
        using var refreshing = _refreshingAuth = new CancellationTokenSource();
        Auth.SetTokenProblem(null);
        RefreshAuthHeader();
        try
        {
            await UpdateAuthSourceAsync();
            if (refreshing.IsCancellationRequested || !HasOAuth)
            {
                return false;
            }
            Pin();
            if (InheritedAuthFolder is not null)
            {
                return await _services.AuthRefresh.RefreshFolderAsync(_inheritedAuth!, environment, refreshing.Token);
            }
            await EnsureOwnIdAsync(refreshing.Token);
            refreshing.Token.ThrowIfCancellationRequested();
            var succeeded = await Auth.FetchTokenAsync(environment, saveSecrets: true, refreshing.Token);
            IsDirty = HasUnsavedChanges();
            return succeeded;
        }
        catch (OperationCanceledException) when (refreshing.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _services.Logger.LogWarning(exception, "Could not refresh the auth of {Name}", Name ?? SuggestedName);
            Auth.SetTokenProblem(exception);
            return false;
        }
        finally
        {
            _refreshingAuth = null;
            RefreshAuthHeader();
        }
    }

    void RefreshAuthHeader()
    {
        OnPropertyChanged(nameof(AuthHeader));
        OnPropertyChanged(nameof(InheritedAuthFolder));
        OnPropertyChanged(nameof(HasInheritedAuth));
        OnPropertyChanged(nameof(AuthSourceTip));
        OnPropertyChanged(nameof(RefreshAuthTip));
        OnPropertyChanged(nameof(HasOAuth));
        OnPropertyChanged(nameof(IsAuthRefreshing));
        OnPropertyChanged(nameof(CanRefreshAuth));
        Send.RaiseCanExecuteChanged();
    }

    public bool IsDirty
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(IsUnsaved));
            }
        }
    }

    public bool IsSending { get; private set => Set(ref field, value); }

    public ResponseDisplay? Response { get; private set => Set(ref field, value); }

    public string? ResponseBodyProblem { get; private set => Set(ref field, value); }

    public IReadOnlyList<Base64Mark> ResponseMarks { get; private set => Set(ref field, value); } = [];

    // Chosen from the Content-Type of each new response, and changed by the user when it does not fit.
    public BodyFormat BodyFormat
    {
        get => _bodyFormat;
        set
        {
            if (Set(ref _bodyFormat, value))
            {
                ShowResponseAgain();
            }
        }
    }

    internal Task Formatting { get; private set; } = Task.CompletedTask;

    public ProblemMessage? Problem { get; private set => Set(ref field, value); }

    // All tabs share one view, so each tab keeps which sections it shows.
    public RequestSection RequestSection { get; set => Set(ref field, value); } = RequestSection.Body;

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
        cancellationToken.ThrowIfCancellationRequested();
        await UpdateAuthSourceAsync();
    }

    public Task SaveAsync() => _services.CollectionChanges.RunAsync(async () =>
    {
        if (_closed)
        {
            return;
        }
        if (Name is { } savedName)
        {
            await SaveCoreAsync(savedName);
            return;
        }
        var translator = _services.Translator;
        var parent = RequestLibrary.ParentOf(SuggestedName) ?? Destination;
        string FullName(string value) => parent is null ? value : $"{parent}/{value}";
        var name = _services.Dialogs.AskName(translator.Of("Save.Title"), RequestLibrary.LastPartOf(SuggestedName ?? DraftName ?? ""), translator.Of("Common.Save"), value => value.Contains('/') ? translator.Of("Save.Invalid") : _services.ProblemOfName(FullName(value)));
        if (name is not null)
        {
            await SaveCoreAsync(FullName(name));
        }
    });

    // The caller holds CollectionChanges, so the path cannot move between choosing it and saving.
    internal async Task SaveCoreAsync(string name)
    {
        if (_closed)
        {
            return;
        }
        var creating = Name is null;
        if (creating && _services.ProblemOfName(name) is { } problem)
        {
            Problem = new(_services.Translator.Of("Save.Failed"), problem);
            return;
        }
        EnsureOwnId();
        try
        {
            // The secrets go first, so a failure leaves no request file behind that points at secrets that were never saved.
            await SaveSecretsAsync(CancellationToken.None);
            var request = ToRequest();
            await (creating ? _services.Library.CreateAsync(name, request, CancellationToken.None) : _services.Library.SaveAsync(name, request, CancellationToken.None));
            _savedJson = SavedJsonOf(request);
            SavedMethod = request.Method;
            Name = name;
            var wasDraft = IsDraft;
            Destination = null;
            IsDraft = false;
            // Edits made while the file was being written are still unsaved.
            IsDirty = HasUnsavedChanges();
            if (wasDraft)
            {
                await UpdateAuthSourceAsync();
            }
            if (creating && Created is { } created)
            {
                foreach (var handler in created.GetInvocationList().Cast<Func<RequestTabViewModel, Task>>())
                {
                    await handler(this);
                }
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save {Name}", name);
            Problem = new(_services.Translator.Of("Save.Failed"), _services.Translator.DetailsOf(exception));
        }
    }

    public void Cancel() => _sending?.Cancel();

    public void Close()
    {
        _closed = true;
        Cancel();
        _refreshingAuth?.Cancel();
        Auth.CancelFetch();
        _services.AuthRefresh.Changed -= RefreshAuthHeader;
    }

    public void Pin()
    {
        _pinned = true;
        OnPropertyChanged(nameof(IsPreview));
    }

    async Task ShowAsync(ApiResponse? response)
    {
        _response = response;
        Response = null;
        ResponseBodyProblem = null;
        ResponseMarks = [];
        if (response is not null)
        {
            Set(ref _bodyFormat, ResponseDisplay.FormatOf(response), nameof(BodyFormat));
            await FormatAsync(response);
        }
    }

    // Formatting a large body takes a while, so it is kept off the UI thread, and a newer response, format or choice of what to decode wins.
    // The one before it is stopped, so quick clicks do not leave several at work.
    async Task FormatAsync(ApiResponse response)
    {
        _formatting?.Cancel();
        using var formatting = _formatting = new CancellationTokenSource();
        var format = _bodyFormat;
        var decode = Base64.Decode;
        try
        {
            var shown = await Task.Run(() => Base64ViewModel.ShowResponse(response, format, decode, _services.Translator, formatting.Token), formatting.Token);
            if (ReferenceEquals(response, _response) && format == _bodyFormat && ReferenceEquals(decode, Base64.Decode))
            {
                Response = shown.Display;
                ResponseMarks = shown.Marks;
                ResponseBodyProblem = shown.Problem;
            }
        }
        catch (OperationCanceledException) when (formatting.IsCancellationRequested)
        {
        }
        finally
        {
            if (_formatting == formatting)
            {
                _formatting = null;
            }
        }
    }

    void ShowResponseAgain()
    {
        if (_response is { } response)
        {
            Formatting = FormatAsync(response);
        }
    }

    public void Rename(string name)
    {
        Name = name;
        _ = UpdateAuthSourceAsync();
    }

    // Laid out off the UI thread, as a large body takes a while, and given to the view rather than set here, so the editor can take it in the way typing is, and it can be undone.
    // A click while one is at work would only do the same work again, so it is left out.
    public async Task<string?> LaidOutBodyAsync()
    {
        if (_layingOut || BodyKind is not (BodyKind.Json or BodyKind.Xml))
        {
            return null;
        }
        _layingOut = true;
        try
        {
            var (body, kind, environment, useVariables) = (Body, BodyKind, EnvironmentOrNone(), UseEnvironmentVariablesInBody);
            var (laidOut, problem) = await Task.Run<(string? LaidOut, LayoutProblem? Problem)>(() =>
                BodyLayout.Of(body, kind, useVariables) is { } text ? (text, null) : (null, ProblemOf(body, kind, environment, useVariables)));
            // The body or how it is interpreted changed while it was laid out.
            if (body != Body || kind != BodyKind || environment != EnvironmentOrNone() || useVariables != UseEnvironmentVariablesInBody)
            {
                return null;
            }
            ShowLayoutProblem(problem);
            return laidOut;
        }
        finally
        {
            _layingOut = false;
        }
    }

    ApiEnvironment EnvironmentOrNone() => _services.Environments.Selected ?? ApiEnvironment.None;

    static LayoutProblem ProblemOf(string body, BodyKind kind, ApiEnvironment environment, bool useVariables) =>
        useVariables && BodyLayout.NeedsVariables(body, kind, environment) ? LayoutProblem.NeedsVariables
        : kind == BodyKind.Xml ? LayoutProblem.NotXml
        : LayoutProblem.NotJson;

    void ShowLayoutProblem(LayoutProblem? problem)
    {
        if (problem != _layoutProblem)
        {
            _layoutProblem = problem;
            OnPropertyChanged(nameof(BodyLayoutProblem));
        }
    }

    public void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BodyLayoutProblem));
        Auth.Relabel();
        RefreshAuthHeader();
        Base64.Relabel();
        // What is written beside the response's properties is in the language too.
        ShowResponseAgain();
    }

    public void Unlink()
    {
        SuggestedName = Name;
        Name = null;
        Destination = null;
        IsDraft = false;
        IsDirty = true;
    }

    public ApiRequest ToRequest() => new()
    {
        Id = Id,
        Method = Method,
        Url = Url,
        Query = Query.ToList(),
        Headers = Headers.ToList(),
        BodyKind = BodyKind,
        Body = Body,
        UseEnvironmentVariablesInBody = UseEnvironmentVariablesInBody,
        Base64 = Base64.ToPaths(),
        Auth = Auth.ToSettings(),
    };

    public async Task SendAsync()
    {
        if (_closed || IsAuthRefreshing)
        {
            return;
        }
        // Sending makes a new call, so the history entry opens the old one again.
        HistoryName = null;
        Problem = null;
        _response = null;
        Response = null;
        ResponseBodyProblem = null;
        ResponseMarks = [];
        IsSending = true;
        // The environment chosen when Send is pressed is the one used, even if another is chosen while the secrets are saved.
        var environment = _services.Environments.Selected;
        using var sending = _sending = new CancellationTokenSource();
        try
        {
            await SaveSecretsAsync(sending.Token);
            // Secrets were all that was unsaved if the request itself is unchanged, such as after fetching a token.
            IsDirty = HasUnsavedChanges();
            var response = await _services.Runner.RunAsync(ToRequest(), Name ?? SuggestedName ?? DraftName, environment, HistorySource.App, _ => RefreshAuthAsync(), sending.Token);
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
            InvalidBase64RequestBodyException => translator.Of("Response.InvalidBase64RequestBody"),
            MissingBase64PathException missing => translator.Format("Response.MissingBase64Path", missing.Path),
            _ => NetworkProblem.Of(exception, translator) ?? cause,
        };
    }

    async Task SaveSecretsAsync(CancellationToken cancellationToken)
    {
        if (!Auth.HasUnsavedSecrets)
        {
            return;
        }
        await EnsureOwnIdAsync(cancellationToken);
        await Auth.SaveSecretsAsync(Id, cancellationToken);
    }

    async Task EnsureOwnIdAsync(CancellationToken cancellationToken)
    {
        if (Name is { } name && OwnsId && await _services.Library.SharesRequestIdAsync(name, Id, cancellationToken))
        {
            _services.Logger.LogWarning("{Name} shares its id with another request, so it gets its own", name);
            TakeNewId();
        }
        EnsureOwnId();
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
        Auth.UseOwner(Id);
        // A new id has no secrets yet, so the ones shown are saved under it.
        Auth.ForgetSavedSecrets();
        MarkDirty();
    }

    bool HasUnsavedChanges() => SavedJsonOf(ToRequest()) != _savedJson || Auth.HasUnsavedSecrets;

    // The lists leave blank rows out, and a tab without Base64 choices leaves them out, so a file with either is compared the same way.
    static string SavedJsonOf(ApiRequest request) => JsonSerializer.Serialize(request with
    {
        Query = KeyValueListViewModel.WithoutBlanks(request.Query),
        Headers = KeyValueListViewModel.WithoutBlanks(request.Headers),
        Base64 = request.Base64 is { Encode: [], Decode: [] } ? null : request.Base64,
    });

    void Load(ApiRequest request)
    {
        if (Id != request.Id || Auth.ToSettings() != request.Auth)
        {
            _refreshingAuth?.Cancel();
        }
        _authResolution++;
        _loading = true;
        _savedJson = SavedJsonOf(request);
        SavedMethod = request.Method;
        Id = request.Id;
        Auth.UseOwner(Id);
        Method = request.Method;
        Url = request.Url;
        Query.Load(request.Query);
        Headers.Load(request.Headers);
        BodyKind = request.BodyKind;
        Body = request.Body;
        UseEnvironmentVariablesInBody = request.UseEnvironmentVariablesInBody;
        Base64.Load(request.Base64, request.Body, request.UseEnvironmentVariablesInBody);
        Auth.Load(request.Auth);
        _loading = false;
        IsDirty = false;
        _inheritedAuth = Name is null && SuggestedName is null && !IsDraft ? new(Id, AuthSettings.None) : null;
        RefreshAuthHeader();
    }

    enum LayoutProblem { NotJson, NotXml, NeedsVariables }

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
