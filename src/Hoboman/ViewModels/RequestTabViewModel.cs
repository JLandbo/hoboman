using System.Net.Http;
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
    bool _loading;
    bool _pinned;
    CancellationTokenSource? _sending;
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
        Auth = new(services.Secrets, services.AuthRefresh, services.Environments, services.Credentials, services.Translator, services.Clock, services.Logger);
        Auth.Changed += MarkDirty;
        Auth.OwnerFetch = RefreshAuthAsync;
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
        Editor = new(services.Translator, services.Clock, services.Environments);
        Editor.Changed += MarkDirty;
        Result = new(services.Translator, Editor.Base64, services.Dialogs);
        // Without an address there is nothing to send, and trying would only leave a failed call in the history.
        Send = new AsyncCommand(SendAsync, () => !string.IsNullOrWhiteSpace(Editor.Url) && !IsAuthRefreshing);
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RequestViewModel.Url))
            {
                Send.RaiseCanExecuteChanged();
            }
        };
        Save = new AsyncCommand(SaveAsync);
        Load(request);
    }

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

    internal string SavedMethod { get; private set; } = "GET";

    public RequestViewModel Editor { get; }

    public AuthViewModel Auth { get; }

    AuthKind? EffectiveAuthKind => Auth.Kind == AuthKind.Inherit ? _inheritedAuth?.Settings.Kind : Auth.Kind;

    public string? InheritedAuthFolder => Auth.Kind == AuthKind.Inherit ? _inheritedAuth?.Folder : null;

    public bool HasInheritedAuth => InheritedAuthFolder is not null;

    public string AuthHeader => AuthViewModel.HeaderOf(EffectiveAuthKind, _services.Translator);

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

    public ResponseViewModel Result { get; }

    public ProblemMessage? Problem { get; private set => Set(ref field, value); }

    // All tabs share one view, so each tab keeps which sections it shows.
    public RequestSection RequestSection { get; set => Set(ref field, value); } = RequestSection.Body;

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
        await Result.ShowAsync(entry.Response);
        Problem = entry.Error is { } error ? new(_services.Translator.Of("Response.Failed"), entry.Problem is { } kind ? HistoryProblemOf(kind) : error) : null;
    }

    // A tab tells of a missing token as when it was sent, not as a workflow step does.
    string HistoryProblemOf(RequestProblemKind kind) =>
        kind == RequestProblemKind.MissingOAuthToken ? _services.Translator.Of("Response.MissingOAuthToken") : RequestProblemTexts.TextOf(kind, _services.Translator);

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
        var name = _services.Dialogs.AskName(translator.Of("Save.Title"), RequestLibrary.LastPartOf(SuggestedName ?? DraftName ?? ""), translator.Of("Common.Save"), _services.OnePart(value => _services.ProblemOfName(FullName(value))));
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

    public void Rename(string name)
    {
        Name = name;
        _ = UpdateAuthSourceAsync();
    }

    ApiEnvironment EnvironmentOrNone() => _services.Environments.SelectedOrNone;

    public void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        RelabelRequest();
        // What is written beside the response's properties is in the language too.
        Result.ShowAgain();
    }

    public void RelabelRequest()
    {
        Editor.Relabel();
        Auth.Relabel();
        RefreshAuthHeader();
    }

    public void Unlink()
    {
        SuggestedName = Name;
        Name = null;
        Destination = null;
        IsDraft = false;
        IsDirty = true;
    }

    public ApiRequest ToRequest() => Editor.ToRequest() with { Id = Id, Auth = Auth.ToSettings() };

    public async Task SendAsync()
    {
        if (_closed || IsAuthRefreshing)
        {
            return;
        }
        // Sending makes a new call, so the history entry opens the old one again.
        HistoryName = null;
        Problem = null;
        await Result.ShowAsync(null);
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
            await Result.ShowAsync(response);
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
        Editor.Load(request);
        Auth.Load(request.Auth);
        _loading = false;
        IsDirty = false;
        _inheritedAuth = Name is null && SuggestedName is null && !IsDraft ? new(Id, AuthSettings.None) : null;
        RefreshAuthHeader();
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
