using System.Globalization;
using Hoboman.Core.Auth;
using Hoboman.Core.Base64;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Workflows;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class WorkflowStepViewModel : ObservableObject
{
    readonly Translator _translator;
    readonly Guid _id;
    StepOutcome? _outcome;
    StepFinished? _finished;
    bool _running;

    public WorkflowStepViewModel(WorkflowStep step, WorkflowServices services)
    {
        _translator = services.Translator;
        Script = step.Script;
        Name = step.Name ?? "";
        if (step.Script is null)
        {
            var request = step.Request ?? new();
            _id = request.Id != Guid.Empty ? request.Id : Guid.NewGuid();
            Request = new(services.Translator, services.Clock, services.Environments);
            Request.Load(request.ToApiRequest());
            Request.Changed += OnChanged;
            Auth = new(services.Secrets, services.AuthRefresh, services.Environments, services.Translator, services.Clock, services.Logger);
            Auth.UseOwner(_id);
            Auth.Load(request.ToApiRequest().Auth);
            Auth.Changed += OnChanged;
            Auth.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AuthViewModel.Kind))
                {
                    OnPropertyChanged(nameof(AuthHeader));
                }
            };
            Request.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RequestViewModel.Method))
                {
                    OnPropertyChanged(nameof(Badge));
                }
                if (e.PropertyName == nameof(RequestViewModel.Url))
                {
                    OnPropertyChanged(nameof(Title));
                }
            };
        }
        // A script has no request to keep what to decode, so its choices last only while the step is shown.
        Result = new(services.Translator, Request?.Base64 ?? new(services.Translator, services.Clock));
        Saves.Load(step.Saves.Select(save => new KeyValue(save.Variable, save.From)));
        Saves.Changed += OnChanged;
    }

    public event Action? Changed;

    public string? Script { get; }

    // The same editors as in a tab. A step has no folder, so its auth does not inherit.
    public RequestViewModel? Request { get; }

    public AuthViewModel? Auth { get; }

    public string AuthHeader => AuthViewModel.HeaderOf(Auth?.Kind, _translator);

    // The owner of the step's secrets, kept in the workflow as the id of its request.
    internal Guid? SecretsId => Auth is null ? null : _id;

    public bool IsRequest => Request is not null;

    // All steps share one view, so each step keeps which section it shows, as a tab does.
    public RequestSection Section { get; set => Set(ref field, value); } = RequestSection.Body;

    public string Name
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Title));
                OnChanged();
            }
        }
    } = "";

    public KeyValueListViewModel Saves { get; } = new();

    public int Number { get; set => Set(ref field, value); }

    // The last step has no line on to a next one in the list.
    public bool IsLast { get; internal set => Set(ref field, value); }

    public string Badge => Request?.Method ?? "JS";

    public string Title => Name.Trim() is { Length: > 0 } name ? name : Script ?? (Request!.Url.Trim() is { Length: > 0 } url ? url : _translator.Of("Workflow.NoUrl"));

    internal IReadOnlySet<string> Used => Request is { } request ? WorkflowCheck.NamesUsedBy(request.ToRequest(), AuthTexts) : new HashSet<string>();

    // The auth is filled in like the rest of the request, so its user name and secret can use names too.
    IEnumerable<string> AuthTexts => Auth?.Kind switch
    {
        AuthKind.Basic => [Auth.UserName, Auth.Password],
        AuthKind.Bearer => [Auth.Token],
        _ => [],
    };

    // The workflow's own names the step uses, and the variables it saves, shown with the step in the list.
    public IReadOnlyList<string> UsedNames { get; internal set => Set(ref field, value); } = [];

    public IReadOnlyList<string> SavedNames { get; internal set => Set(ref field, value); } = [];

    // Only the run started in this editor is shown, and nothing of it is kept.
    public string? Status { get; private set => Set(ref field, value); }

    public bool IsSuccess { get; private set => Set(ref field, value); }

    public string? Elapsed { get; private set => Set(ref field, value); }

    public string? State => _running ? _translator.Of("Workflow.Running")
        : Status is not null ? null
        : _outcome switch
        {
            StepOutcome.Failed => _translator.Of("Workflow.Failed"),
            StepOutcome.Skipped => _translator.Of("Workflow.Skipped"),
            StepOutcome.Cancelled => _translator.Of("Workflow.Cancelled"),
            _ => null,
        };

    // The same response view as in a tab.
    public ResponseViewModel Result { get; }

    // Told by the kind of problem, as in the run log, so it never holds a value.
    public string? Error => _finished is null ? null : ErrorOf(_finished);

    public ProblemMessage? Problem => Error is { } error ? new(_translator.Of("Workflow.StepFailed"), error) : null;

    public bool IsSending => _running;

    public bool HasFailed => _outcome == StepOutcome.Failed;

    public WorkflowStep ToStep() => new()
    {
        Name = Name.Trim() is { Length: > 0 } name ? name : null,
        Request = Request is { } request ? WorkflowRequest.From(request.ToRequest() with { Id = _id, Auth = Auth!.ToSettings() }) : null,
        Script = Script,
        Saves = [.. Saves.ToList().Select(save => new WorkflowSave(save.Name, save.Value))],
    };

    void OnChanged() => Changed?.Invoke();

    // Secrets that cannot be read are shown as empty, as in a tab.
    internal Task LoadSecretsAsync(CancellationToken cancellationToken) => Auth?.LoadSecretsAsync(_id, cancellationToken) ?? Task.CompletedTask;

    internal Task SaveSecretsAsync(CancellationToken cancellationToken) => Auth is { HasUnsavedSecrets: true } auth ? auth.SaveSecretsAsync(_id, cancellationToken) : Task.CompletedTask;

    internal void Close() => Auth?.CancelFetch();

    internal void ClearRun()
    {
        _outcome = null;
        _running = false;
        Status = null;
        IsSuccess = false;
        Elapsed = null;
        Result.Saved = new Dictionary<string, string>();
        _ = Result.ShowAsync(null);
        _finished = null;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsSending));
        OnPropertyChanged(nameof(HasFailed));
    }

    internal void Started()
    {
        _running = true;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsSending));
    }

    internal void Ended(StepOutcome outcome)
    {
        _running = false;
        _outcome = outcome;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsSending));
        OnPropertyChanged(nameof(HasFailed));
    }

    internal async Task FinishedAsync(StepFinished finished, IReadOnlyList<WorkflowSave> saves)
    {
        IsSuccess = finished.Outcome == StepOutcome.Succeeded;
        Status = finished.Status?.ToString(CultureInfo.InvariantCulture);
        Elapsed = finished.ElapsedMs is { } elapsed ? $"{elapsed} ms" : null;
        _finished = finished;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(Problem));
        Ended(finished.Outcome);
        if (finished.Status is { } status)
        {
            Result.Saved = SavedPlacesOf(finished, saves);
            await Result.ShowAsync(new(status, finished.Reason ?? "", finished.ElapsedMs ?? 0, finished.Size ?? 0, finished.Headers ?? [], finished.Body ?? ""));
        }
    }

    // Only what was saved is shown, so a place without a mark was not saved from.
    static IReadOnlyDictionary<string, string> SavedPlacesOf(StepFinished finished, IReadOnlyList<WorkflowSave> saves) => finished.Saved is { } saved
        ? saves.Where(save => saved.ContainsKey(save.Variable)).Select(save => (save.Variable, Place: JsonPath.PlaceOf(save.From))).Where(save => save.Place is not null)
            .GroupBy(save => save.Place!).ToDictionary(places => places.Key, places => string.Join(", ", places.Select(save => save.Variable)))
        : new Dictionary<string, string>();

    string? ErrorOf(StepFinished finished) => finished switch
    {
        { MissingSave: { } path } => _translator.Format("Workflow.MissingSave", path),
        { Problem: RequestProblemKind.Cancelled } => _translator.Of("RequestProblem.Cancelled"),
        { Problem: RequestProblemKind.TimedOut } => _translator.Of("RequestProblem.TimedOut"),
        { Problem: RequestProblemKind.MissingOAuthToken } => _translator.Of("RequestProblem.MissingOAuthToken"),
        { Problem: RequestProblemKind.MissingSecret } => _translator.Of("RequestProblem.MissingSecret"),
        { Problem: RequestProblemKind.InvalidUrl } => _translator.Of("RequestProblem.InvalidUrl"),
        { Problem: RequestProblemKind.NetworkFailed } => _translator.Of("RequestProblem.NetworkFailed"),
        { Problem: RequestProblemKind.InvalidInput } => _translator.Of("RequestProblem.InvalidInput"),
        { Problem: RequestProblemKind.BodyNotEncoded } => _translator.Of("RequestProblem.BodyNotEncoded"),
        { Problem: RequestProblemKind.InputUnreadable } => _translator.Of("RequestProblem.InputUnreadable"),
        { Problem: not null } => _translator.Of("RequestProblem.Failed"),
        // A script tells of its own error by file and line, as written by the script.
        { Error: { } error } => error,
        _ => null,
    };

    public void Relabel()
    {
        Request?.Relabel();
        Auth?.Relabel();
        OnPropertyChanged(nameof(AuthHeader));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(Problem));
        Result.ShowAgain();
    }
}
