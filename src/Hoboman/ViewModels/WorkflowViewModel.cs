using System.Collections.ObjectModel;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Core.Text;
using Hoboman.Core.Workflows;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class WorkflowViewModel : ObservableObject
{
    readonly WorkflowServices _services;
    readonly RequestTreeViewModel _tree;
    Guid _id;
    string _savedJson = "";
    int _version;
    bool _closed;
    bool _reloadAfterRun;
    CancellationTokenSource? _running;

    public WorkflowViewModel(WorkflowServices services, RequestTreeViewModel tree, string name)
    {
        _services = services;
        _tree = tree;
        Name = name;
        Parameters.Changed += Edited;
        Variables.Changed += Edited;
        Send = new AsyncCommand(RunAsync, () => !IsRunning);
        Save = new AsyncCommand(SaveAsync);
    }

    public string Name { get; private set => Set(ref field, value); }

    public KeyValueListViewModel Parameters { get; } = new();

    public KeyValueListViewModel Variables { get; } = new();

    public ObservableCollection<WorkflowStepViewModel> Steps { get; } = [];

    public WorkflowStepViewModel? SelectedStep { get; set => Set(ref field, value); }

    // The saved requests in the order of the tree, to choose a step from.
    public IEnumerable<string> RequestNames => RequestTreeViewModel.Flatten(_tree.Nodes).Where(node => !node.IsFolder && !node.IsDraft).Select(node => node.Path);

    public bool IsDirty { get; private set => Set(ref field, value); }

    public bool IsRunning
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

    // What keeps the workflow from running, found before anything is sent.
    public IReadOnlyList<string> Problems { get; private set => Set(ref field, value); } = [];

    public AsyncCommand Send { get; }

    public AsyncCommand Save { get; }

    // Gives false when the workflow is not there or cannot be read.
    public async Task<bool> LoadAsync()
    {
        try
        {
            if (await _services.Library.LoadAsync(Name, CancellationToken.None) is not { } workflow)
            {
                return false;
            }
            Load(workflow);
            await RefreshRequestsAsync();
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not open the workflow {Name}", Name);
            _services.Dialogs.Tell(_services.Translator.Of("Workflow.OpenFailed"), _services.Translator.DetailsOf(exception));
            return false;
        }
    }

    // Changes on disk are taken in when nothing is edited here, but the editor's own save must not clear the run that is shown.
    // A change seen during a run is taken in when the run ends, as the run shows its results on the steps it started with.
    // Gives false when the workflow is gone.
    public async Task<bool> ReloadAsync()
    {
        try
        {
            if (await _services.Library.LoadAsync(Name, CancellationToken.None) is not { } workflow)
            {
                return false;
            }
            if (IsRunning)
            {
                _reloadAfterRun = true;
            }
            else if (!IsDirty && JsonOf(workflow) != _savedJson)
            {
                _services.Logger.LogInformation("The workflow {Name} changed on disk and was reloaded", Name);
                Load(workflow);
                await RefreshRequestsAsync();
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogWarning(exception, "Could not reload the workflow {Name}", Name);
        }
        return true;
    }

    // The steps keep only the id of their request, so its path and the names it uses are looked up again when the requests change.
    public async Task RefreshRequestsAsync()
    {
        foreach (var step in Steps.ToList())
        {
            await RefreshRequestAsync(step);
        }
        Refresh();
    }

    public void Rename(string name) => Name = name;

    public void Relabel()
    {
        foreach (var step in Steps)
        {
            step.Relabel();
        }
        Refresh();
    }

    public void Close()
    {
        _closed = true;
        Cancel();
    }

    public void Cancel() => _running?.Cancel();

    public async Task AddStepAsync(string path)
    {
        var id = _tree.IdOf(path);
        if (id == Guid.Empty)
        {
            _services.Dialogs.Tell(_services.Translator.Of("Workflow.NoIdTitle"), _services.Translator.Of("Workflow.NoId"));
            return;
        }
        // An id that a copy made outside Hoboman shares points at neither file for sure.
        if (_tree.PathOf(id) is null)
        {
            _services.Dialogs.Tell(_services.Translator.Of("Workflow.SharedIdTitle"), _services.Translator.Of("Workflow.SharedId"));
            return;
        }
        var step = Follow(new(new() { Request = id }, _services.Translator));
        Steps.Add(step);
        SelectedStep = step;
        Edited();
        await RefreshRequestAsync(step);
        Refresh();
    }

    public void RemoveStep(WorkflowStepViewModel step)
    {
        var index = Steps.IndexOf(step);
        if (index < 0)
        {
            return;
        }
        Unfollow(step);
        var wasSelected = SelectedStep == step;
        Steps.RemoveAt(index);
        if (wasSelected)
        {
            SelectedStep = Steps.ElementAtOrDefault(Math.Min(index, Steps.Count - 1));
        }
        Edited();
    }

    public void MoveStep(WorkflowStepViewModel step, int offset)
    {
        var index = Steps.IndexOf(step);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Steps.Count)
        {
            return;
        }
        Steps.Move(index, target);
        Edited();
    }

    public async Task SaveAsync()
    {
        if (_closed)
        {
            return;
        }
        var translator = _services.Translator;
        if (InvalidDefault() is { } name)
        {
            _services.Dialogs.Tell(translator.Of("Workflow.SaveFailed"), translator.Format("Workflow.InvalidDefault", name));
            return;
        }
        var version = _version;
        var workflow = ToWorkflow();
        try
        {
            // A workflow that was renamed or deleted on disk is not brought back under its old name.
            await _services.Library.SaveAsync(Name, workflow, CancellationToken.None, createDirectory: false);
            _savedJson = JsonOf(workflow);
            // Edits made while the file was written are still unsaved.
            IsDirty = _version != version;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save the workflow {Name}", Name);
            _services.Dialogs.Tell(translator.Of("Workflow.SaveFailed"), translator.DetailsOf(exception));
        }
    }

    // The workflow runs as it is in the editor, and its requests as they are saved.
    public async Task RunAsync()
    {
        if (_closed || IsRunning)
        {
            return;
        }
        var translator = _services.Translator;
        Problems = [];
        foreach (var step in Steps)
        {
            step.ClearRun();
        }
        if (InvalidDefault() is { } invalid)
        {
            Problems = [translator.Format("Workflow.InvalidDefault", invalid)];
            return;
        }
        var parameters = new Dictionary<string, JsonElement>();
        var missing = ValuesOf(Parameters).Where(parameter => !parameter.HasDefault && WorkflowCheck.IsValidName(parameter.Name)).Select(parameter => parameter.Name).Distinct().ToList();
        if (missing.Count > 0)
        {
            if (_services.Dialogs.AskValues(translator.Format("Workflow.RunTitle", Name), missing, translator.Of("Workflow.Run")) is not { } values)
            {
                return;
            }
            foreach (var (name, value) in values)
            {
                parameters[name] = JsonSerializer.SerializeToElement(value);
            }
        }
        // The workflow can be reloaded or closed while the values are asked for, so it is taken from the editor only now.
        if (_closed)
        {
            return;
        }
        var workflow = ToWorkflow();
        var environment = _services.Environments.Selected ?? ApiEnvironment.None;
        var steps = Steps.ToList();
        IsRunning = true;
        using var running = _running = new CancellationTokenSource();
        try
        {
            var checkedWorkflow = await _services.Check.CheckAsync(Name, workflow, environment, parameters, running.Token);
            if (checkedWorkflow.Problems.Count > 0)
            {
                Problems = [.. checkedWorkflow.Problems.Select(TextOf)];
                return;
            }
            await _services.Runner.RunAsync(checkedWorkflow, environment, HistorySource.App, auth => FetchTokenAsync(auth, environment, running.Token), workflowEvent => ShowAsync(workflowEvent, steps),
                running.Token);
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not run the workflow {Name}", Name);
            Problems = [translator.DetailsOf(exception)];
        }
        finally
        {
            _running = null;
            IsRunning = false;
            if (_reloadAfterRun && !_closed)
            {
                _reloadAfterRun = false;
                await ReloadAsync();
            }
        }
    }

    static async Task ShowAsync(WorkflowEvent workflowEvent, IReadOnlyList<WorkflowStepViewModel> steps)
    {
        switch (workflowEvent)
        {
            case StepStarted started:
                steps[started.Index].Started();
                break;
            case StepFinished finished:
                await steps[finished.Index].FinishedAsync(finished);
                break;
            case StepSkipped skipped:
                steps[skipped.Index].Ended(StepOutcome.Skipped);
                break;
            case StepCancelled cancelled:
                steps[cancelled.Index].Ended(StepOutcome.Cancelled);
                break;
        }
    }

    // Only client credentials come here, as they need no login. The token is fetched for the chosen environment and saved with whoever owns the auth, as from its tab or folder.
    // A failed fetch is only logged, so the step tells of the problem that made it fetch.
    async Task<bool> FetchTokenAsync(AuthSource auth, ApiEnvironment environment, CancellationToken cancellationToken)
    {
        try
        {
            return auth.Folder is not null
                ? await _services.AuthRefresh.RefreshFolderAsync(auth, environment, cancellationToken)
                : auth.SecretsId != Guid.Empty && await _services.AuthRefresh.FetchAsync(auth, null, environment, async (token, environmentId, _) =>
                {
                    await _services.Secrets.SaveAsync(auth.SecretsId, SecretKind.OAuthToken, environmentId, token.ToJson(), cancellationToken);
                    return true;
                }, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            _services.Logger.LogWarning(exception, "Could not fetch an OAuth token for the workflow {Name}", Name);
            return false;
        }
    }

    string TextOf(WorkflowProblem problem)
    {
        var translator = _services.Translator;
        var text = problem.Kind switch
        {
            WorkflowProblemKind.MissingId => translator.Of("WorkflowProblem.MissingId"),
            WorkflowProblemKind.InvalidName => translator.Format("WorkflowProblem.InvalidName", problem.Detail),
            WorkflowProblemKind.DuplicateName => translator.Format("WorkflowProblem.DuplicateName", problem.Detail),
            WorkflowProblemKind.UnknownParameter => translator.Format("WorkflowProblem.UnknownParameter", problem.Detail),
            WorkflowProblemKind.MissingParameter => translator.Format("WorkflowProblem.MissingParameter", problem.Detail),
            WorkflowProblemKind.MissingRequest => translator.Of("WorkflowProblem.MissingRequest"),
            WorkflowProblemKind.RequestNotFound => translator.Format("WorkflowProblem.RequestNotFound", problem.Detail),
            WorkflowProblemKind.UnreadableRequests => translator.Format("WorkflowProblem.UnreadableRequests", problem.Detail),
            WorkflowProblemKind.SharedRequestId => translator.Format("WorkflowProblem.SharedRequestId", problem.Detail),
            WorkflowProblemKind.NotAVariable => translator.Format("WorkflowProblem.NotAVariable", problem.Detail),
            WorkflowProblemKind.InvalidSource => translator.Format("WorkflowProblem.InvalidSource", problem.Detail),
            WorkflowProblemKind.UnusedWithName => translator.Format("WorkflowProblem.UnusedWithName", problem.Detail),
            WorkflowProblemKind.UsedBeforeSaved => translator.Format("WorkflowProblem.UsedBeforeSaved", problem.Detail),
            _ => translator.Format("WorkflowProblem.UnknownName", problem.Detail),
        };
        return problem.Step is { } step ? translator.Format("Workflow.StepProblem", step + 1, text) : text;
    }

    // Where each name a step uses gets its value, by the rule of the check: a value on the step, then the workflow's own names, and only then the environment.
    void Refresh()
    {
        var translator = _services.Translator;
        var parameters = Parameters.ToList().Select(parameter => parameter.Name).ToHashSet();
        var variables = Variables.ToList();
        var variableNames = variables.Select(variable => variable.Name).ToHashSet();
        var defaults = variables.Where(variable => !string.IsNullOrWhiteSpace(variable.Value)).Select(variable => variable.Name).ToHashSet();
        var environment = _services.Environments.Selected;
        var savedIn = new Dictionary<string, int>();
        foreach (var (index, step) in Steps.Index())
        {
            step.Number = index + 1;
            var given = step.With.ToList().Where(entry => entry.Enabled).ToList();
            var inValues = given.SelectMany(entry => WorkflowCheck.NamesIn(entry.Value)).ToHashSet();
            var names = step.Used.Concat(inValues).Distinct().Order(StringComparer.Ordinal).ToList();
            // The values on a step do not see each other, so a name used in one of them must get its value from outside the step.
            step.Uses = [.. names.Select(name => UseOf(name, step.Used.Contains(name) && !inValues.Contains(name) && given.Any(entry => entry.Name == name)))];
            var saved = step.Saves.ToList().Select(save => save.Name).ToList();
            var used = names.Where(name => parameters.Contains(name) || variableNames.Contains(name)).ToList();
            step.Summary = string.Join(" · ", new[]
            {
                used.Count > 0 ? translator.Format("Workflow.StepUses", string.Join(", ", used)) : null,
                saved.Count > 0 ? translator.Format("Workflow.StepSaves", string.Join(", ", saved)) : null,
            }.OfType<string>());
            foreach (var name in saved.Where(variableNames.Contains))
            {
                savedIn[name] = index + 1;
            }
        }

        WorkflowUse UseOf(string name, bool given)
        {
            var source = given ? translator.Of("Workflow.FromThisStep")
                : parameters.Contains(name) ? translator.Of("Workflow.FromParameter")
                : savedIn.TryGetValue(name, out var number) ? translator.Format("Workflow.FromStep", number)
                : defaults.Contains(name) ? translator.Of("Workflow.FromDefault")
                : !variableNames.Contains(name) && environment?.Variables.Any(variable => variable.Enabled && variable.Name == name) == true ? translator.Format("Workflow.FromEnvironment", environment.Name)
                : null;
            return new($"{{{{{name}}}}}", source ?? translator.Of("Workflow.Missing"), source is null);
        }
    }

    async Task RefreshRequestAsync(WorkflowStepViewModel step)
    {
        if (_tree.PathOf(step.Request) is not { } path)
        {
            step.Show(null, null, new HashSet<string>());
            return;
        }
        try
        {
            var request = await _services.Requests.LoadAsync(path, CancellationToken.None);
            step.Show(path, request?.Method, request is null ? new HashSet<string>() : await _services.Check.NamesUsedByAsync(path, request, CancellationToken.None));
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogWarning(exception, "Could not read {Name} for the workflow {Workflow}", path, Name);
            step.Show(path, null, new HashSet<string>());
        }
    }

    void Load(Workflow workflow)
    {
        _id = workflow.Id;
        _savedJson = JsonOf(workflow);
        Parameters.Load(workflow.Parameters.Select(EntryOf));
        Variables.Load(workflow.Variables.Select(EntryOf));
        foreach (var step in Steps)
        {
            Unfollow(step);
        }
        Steps.Clear();
        foreach (var step in workflow.Steps)
        {
            Steps.Add(Follow(new(step, _services.Translator)));
        }
        SelectedStep = Steps.FirstOrDefault();
        Problems = [];
        IsDirty = false;
    }

    WorkflowStepViewModel Follow(WorkflowStepViewModel step)
    {
        step.With.Changed += Edited;
        step.Saves.Changed += Edited;
        return step;
    }

    void Unfollow(WorkflowStepViewModel step)
    {
        step.With.Changed -= Edited;
        step.Saves.Changed -= Edited;
    }

    void Edited()
    {
        _version++;
        IsDirty = true;
        Refresh();
    }

    Workflow ToWorkflow() => new()
    {
        Id = _id,
        Parameters = ValuesOf(Parameters),
        Variables = ValuesOf(Variables),
        Steps = [.. Steps.Select(step => step.ToStep())],
    };

    string? InvalidDefault() => Parameters.ToList().Concat(Variables.ToList()).FirstOrDefault(value => !TryDefaultOf(value.Value, out _))?.Name;

    static IReadOnlyList<WorkflowValue> ValuesOf(KeyValueListViewModel values) =>
        [.. values.ToList().Select(value => new WorkflowValue(value.Name) { Default = TryDefaultOf(value.Value, out var json) ? json : default })];

    // A default is JSON, so 50 is a number and "50" a text, and a blank one is no default.
    static bool TryDefaultOf(string text, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        try
        {
            value = JsonSerializer.Deserialize<JsonElement>(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static KeyValue EntryOf(WorkflowValue value) => new(value.Name, value.HasDefault ? JsonSerializer.Serialize(value.Default, CompactJson.Options) : "");

    static string JsonOf(Workflow workflow) => JsonSerializer.Serialize(workflow, CompactJson.Options);
}
