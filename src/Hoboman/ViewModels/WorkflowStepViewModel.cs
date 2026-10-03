using System.Globalization;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Workflows;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class WorkflowStepViewModel : ObservableObject
{
    readonly Translator _translator;
    StepOutcome? _outcome;
    StepFinished? _finished;
    bool _running;

    public WorkflowStepViewModel(WorkflowStep step, Translator translator)
    {
        _translator = translator;
        Request = step.Request;
        With.Load(step.With);
        Saves.Load(step.Saves.Select(save => new KeyValue(save.Variable, save.From)));
    }

    // Only the id is kept, so the step follows its request when it is moved or renamed.
    public Guid Request { get; }

    public KeyValueListViewModel With { get; } = new();

    public KeyValueListViewModel Saves { get; } = new();

    public int Number { get; set => Set(ref field, value); }

    public string? Path
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

    public string? Method { get; private set => Set(ref field, value); }

    internal IReadOnlySet<string> Used { get; private set; } = new HashSet<string>();

    public string Title => Path is { } path ? RequestLibrary.LastPartOf(path) : _translator.Of("Workflow.RequestNotFound");

    public string? Folder => RequestLibrary.ParentOf(Path) is { } parent ? $"{parent.Replace("/", " / ")} /" : null;

    public string Summary { get; internal set => Set(ref field, value); } = "";

    public IReadOnlyList<WorkflowUse> Uses { get; internal set => Set(ref field, value); } = [];

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

    public ResponseDisplay? Response { get; private set => Set(ref field, value); }

    // Told by the kind of problem, as in the run log, so it never holds a value.
    public string? Error => _finished is null ? null : ErrorOf(_finished);

    public WorkflowStep ToStep() => new() { Request = Request, With = With.ToList(), Saves = [.. Saves.ToList().Select(save => new WorkflowSave(save.Name, save.Value))] };

    internal void Show(string? path, string? method, IReadOnlySet<string> used)
    {
        Path = path;
        Method = method;
        Used = used;
    }

    internal void ClearRun()
    {
        _outcome = null;
        _running = false;
        Status = null;
        IsSuccess = false;
        Elapsed = null;
        Response = null;
        _finished = null;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(State));
    }

    internal void Started()
    {
        _running = true;
        OnPropertyChanged(nameof(State));
    }

    internal void Ended(StepOutcome outcome)
    {
        _running = false;
        _outcome = outcome;
        OnPropertyChanged(nameof(State));
    }

    internal async Task FinishedAsync(StepFinished finished)
    {
        IsSuccess = finished.Outcome == StepOutcome.Succeeded;
        Status = finished.Status?.ToString(CultureInfo.InvariantCulture);
        Elapsed = finished.ElapsedMs is { } elapsed ? $"{elapsed} ms" : null;
        _finished = finished;
        OnPropertyChanged(nameof(Error));
        Ended(finished.Outcome);
        if (finished.Status is { } status)
        {
            var response = new ApiResponse(status, finished.Reason ?? "", finished.ElapsedMs ?? 0, finished.Size ?? 0, finished.Headers ?? [], finished.Body ?? "");
            // A large body takes a while to lay out, so it is done off the UI thread.
            Response = await Task.Run(() => ResponseDisplay.Of(response));
        }
    }

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
        _ => null,
    };

    public void Relabel()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Error));
    }
}
