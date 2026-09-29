using System.Runtime.CompilerServices;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.History;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed record RequestTabServices(RequestRunner Runner, SecretStore Secrets, RequestLibrary Library, EnvironmentsViewModel Environments, IDialogs Dialogs, Translator Translator, ILogger<RequestTabViewModel> Logger)
{
    public string? ProblemOfName(string name) =>
        !RequestLibrary.IsValidName(name) ? Translator.Of("Save.Invalid")
        : Library.Exists(name) ? Translator.Of("Save.Exists")
        : null;
}

public sealed record ProblemMessage(string Title, string Details);

public sealed class RequestTabViewModel : ObservableObject
{
    readonly RequestTabServices _services;
    Guid _id;
    bool _ownsId;
    OAuthSettings? _oauth;
    string _savedJson = "";
    bool _loading;
    bool _passwordChanged;
    bool _tokenChanged;
    CancellationTokenSource? _sending;

    public RequestTabViewModel(RequestTabServices services, ApiRequest request, string? name = null, string? suggestedName = null, bool fromHistory = false)
    {
        _services = services;
        Name = name;
        SuggestedName = suggestedName;
        FromHistory = fromHistory;
        _ownsId = !fromHistory;
        Query.Changed += MarkDirty;
        Headers.Changed += MarkDirty;
        Send = new AsyncCommand(SendAsync, () => !IsSending);
        Save = new AsyncCommand(async () => await SaveAsync());
        Cancel = new Command(_ => _sending?.Cancel());
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

    public bool FromHistory { get; }

    public string? Title => (Name ?? SuggestedName)?.Split('/')[^1];

    public string? Folder => Name?.Contains('/') == true ? $"{Name[..Name.LastIndexOf('/')].Replace("/", " / ")} /" : null;

    public string Method { get; set => Change(ref field, value); } = "GET";

    public string Url { get; set => Change(ref field, value); } = "";

    public KeyValueListViewModel Query { get; } = new();

    public KeyValueListViewModel Headers { get; } = new();

    public BodyKind BodyKind { get; set => Change(ref field, value); }

    public string Body { get; set => Change(ref field, value); } = "";

    public AuthKind AuthKind { get; set => Change(ref field, value); }

    public string UserName { get; set => Change(ref field, value); } = "";

    public string Password
    {
        get;
        set
        {
            if (Change(ref field, value))
            {
                _passwordChanged |= !_loading;
            }
        }
    } = "";

    public string Token
    {
        get;
        set
        {
            if (Change(ref field, value))
            {
                _tokenChanged |= !_loading;
            }
        }
    } = "";

    public bool IsDirty { get; private set => Set(ref field, value); }

    public bool IsSending
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Send.RaiseCanExecuteChanged();
            }
        }
    }

    public ResponseDisplay? Response { get; private set => Set(ref field, value); }

    public ProblemMessage? Problem { get; private set => Set(ref field, value); }

    public AsyncCommand Send { get; }

    public AsyncCommand Save { get; }

    public Command Cancel { get; }

    // Changes on disk win over unsaved changes, but the tab's own saves must not reload it.
    public bool ReloadIfChanged(ApiRequest request)
    {
        if (JsonSerializer.Serialize(request) == _savedJson)
        {
            return false;
        }
        Load(request);
        _passwordChanged = _tokenChanged = false;
        return true;
    }

    public void ShowProblem(ProblemMessage problem) => Problem = problem;

    public void Show(HistoryEntry entry)
    {
        Response = entry.Response is { } response ? ResponseDisplay.Of(response) : null;
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
        Password = password ?? "";
        Token = token ?? "";
        _loading = false;
    }

    public async Task<bool> SaveAsync()
    {
        var translator = _services.Translator;
        var name = Name ?? _services.Dialogs.AskName(translator.Of("Save.Title"), SuggestedName ?? "", translator.Of("Editor.Save"), _services.ProblemOfName);
        if (name is null)
        {
            return false;
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
            IsDirty = false;
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save {Name}", name);
            Problem = new(translator.Of("Save.Failed"), exception.Message);
            return false;
        }
    }

    public void Rename(string name) => Name = name;

    public void Unlink()
    {
        SuggestedName = Name;
        Name = null;
        IsDirty = true;
    }

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
            Response = ResponseDisplay.Of(await _services.Runner.RunAsync(ToRequest(), Name, _services.Environments.Selected, HistorySource.App, sending.Token));
        }
        catch (OperationCanceledException) when (sending.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Problem = new(_services.Translator.Of("Response.Failed"), ProblemOf(exception));
        }
        finally
        {
            _sending = null;
            IsSending = false;
        }
    }

    string ProblemOf(Exception exception) => exception switch
    {
        MissingSecretException { Kind: SecretKind.Password } => _services.Translator.Of("Response.MissingPassword"),
        MissingSecretException => _services.Translator.Of("Response.MissingToken"),
        _ => exception.Message,
    };

    async Task SaveSecretsAsync(CancellationToken cancellationToken)
    {
        if (!_passwordChanged && !_tokenChanged)
        {
            return;
        }
        EnsureOwnId();
        if (_passwordChanged)
        {
            await _services.Secrets.SaveAsync(_id, SecretKind.Password, Password, cancellationToken);
        }
        if (_tokenChanged)
        {
            await _services.Secrets.SaveAsync(_id, SecretKind.Token, Token, cancellationToken);
        }
        _passwordChanged = _tokenChanged = false;
    }

    void EnsureOwnId()
    {
        if (_ownsId && _id != Guid.Empty)
        {
            return;
        }
        _id = Guid.NewGuid();
        _ownsId = true;
        _passwordChanged |= Password.Length > 0;
        _tokenChanged |= Token.Length > 0;
        MarkDirty();
    }

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
        if (!_loading)
        {
            IsDirty = true;
        }
    }
}
