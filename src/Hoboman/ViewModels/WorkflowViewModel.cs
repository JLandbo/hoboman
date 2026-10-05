using System.Collections.ObjectModel;
using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
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
    readonly Guid _id;
    string _savedJson = "";
    int _version;
    bool _closed;
    bool _reloadPostponed;
    int _fetchesByHand;
    CancellationTokenSource? _running;
    // Kept per file, so two steps that run the same script show the same code. File names on Windows ignore case.
    readonly Dictionary<string, string> _code = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _savedCode = new(StringComparer.OrdinalIgnoreCase);
    // The names the steps save into, which are the workflow's variables.
    IReadOnlyList<string> _variables = [];
    // A default written by hand in the file is kept, as the app does not show the variables.
    IReadOnlyDictionary<string, JsonElement> _variableDefaults = new Dictionary<string, JsonElement>();
    // The steps in the file as it was last loaded or saved, which take their secrets with them when they are removed here.
    IReadOnlySet<Guid> _savedOwners = new HashSet<Guid>();
    // Steps whose secrets were saved before the steps themselves were, as a run does, so the secrets go if the steps never are saved.
    readonly HashSet<Guid> _unsavedOwners = [];

    // The name is read from the file, and the id says which file.
    public WorkflowViewModel(WorkflowServices services, Guid id)
    {
        _services = services;
        _id = id;
        Parameters.Changed += Edited;
        Auth = new(services.Secrets, services.AuthRefresh, services.Environments, services.Credentials, services.Translator, services.Clock, services.Logger);
        Auth.Changed += Edited;
        Auth.OwnerFetch = () => FetchByHandAsync(Auth);
        Auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AuthViewModel.Kind) or nameof(AuthViewModel.IsFetching))
            {
                RelabelAuth();
            }
        };
        Send = new AsyncCommand(RunAsync, () => !IsRunning);
        Save = new AsyncCommand(SaveAsync);
    }

    public Guid Id => _id;

    public string Name { get; private set => Set(ref field, value); } = "";

    public KeyValueListViewModel Parameters { get; } = new();

    // The auth the steps that inherit use, as a folder's auth is for its requests.
    public AuthViewModel Auth { get; }


    public bool HasOAuth => Auth.Kind == AuthKind.OAuth2;

    public bool IsAuthRefreshing => Auth.IsFetching;

    public bool CanRefreshAuth => !_closed && HasOAuth && !IsAuthRefreshing;

    public string RefreshAuthTip => _services.Translator.Format("OAuth.Reauthenticate", Name, _services.Environments.Selected?.Name ?? _services.Translator.Of("Environment.None"));

    public ObservableCollection<WorkflowStepViewModel> Steps { get; } = [];

    public WorkflowStepViewModel? SelectedStep
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Code));
            }
        }
    }

    // The code of the selected script step, as it is in the editor.
    public string? Code
    {
        get => SelectedStep?.Script is { } script ? _code.GetValueOrDefault(script, "") : null;
        set
        {
            if (SelectedStep?.Script is { } script && WorkflowLibrary.IsValidScriptName(script) && value is not null && value != Code)
            {
                _code[script] = value;
                OnPropertyChanged();
                Edited();
            }
        }
    }

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

    // How the last run went, shown until the next one starts or the workflow is loaded again.
    RunFinished? LastRun
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasSucceeded));
        }
    }

    public string? Summary => LastRun is { } run
        ? _services.Translator.Format("Workflow.Summary", run.Steps.Count(step => step.Outcome == StepOutcome.Succeeded), run.Steps.Count, run.ElapsedMs / 1000d)
        : null;

    public bool HasSucceeded => LastRun?.Outcome == RunOutcome.Succeeded;

    public AsyncCommand Send { get; }

    // Told when a run comes to a step, so the step can be brought into view.
    public event Action<WorkflowStepViewModel>? StepRunning;

    public AsyncCommand Save { get; }

    // Gives false when the workflow is not there or cannot be read.
    public async Task<bool> LoadAsync()
    {
        try
        {
            if (await _services.Library.LoadAsync(_id, CancellationToken.None) is not { } workflow)
            {
                return false;
            }
            Load(workflow);
            await ReadCodeAsync(Scripts);
            await LoadSecretsAsync();
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
    // The same goes for a token fetched by hand, as a login can stay open for minutes and its token belongs to the step that waits for it.
    // Gives false when the workflow is gone.
    public async Task<bool> ReloadAsync()
    {
        try
        {
            if (await _services.Library.LoadAsync(_id, CancellationToken.None) is not { } workflow)
            {
                return false;
            }
            // The name is saved by itself, so it is taken in even while the workflow is edited, as a tab takes its request's name.
            if (workflow.Name != Name)
            {
                Rename(workflow.Name);
            }
            if (IsRunning || _fetchesByHand > 0)
            {
                _reloadPostponed = true;
            }
            else
            {
                if (!IsDirty && JsonOf(workflow) != _savedJson)
                {
                    _services.Logger.LogInformation("The workflow {Name} changed on disk and was reloaded", Name);
                    Load(workflow);
                    await LoadSecretsAsync();
                }
                // A script may have been changed in another editor. It is taken in unless it is edited here, even while other things are.
                await ReadCodeAsync([.. Scripts.Where(script => _code.GetValueOrDefault(script) == _savedCode.GetValueOrDefault(script))]);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogWarning(exception, "Could not reload the workflow {Name}", Name);
        }
        return true;
    }

    public void Rename(string name)
    {
        Name = name;
        RelabelAuth();
    }

    public void Relabel()
    {
        Auth.Relabel();
        foreach (var step in Steps)
        {
            step.Relabel();
        }
        OnPropertyChanged(nameof(Summary));
        Refresh();
    }

    public void EnvironmentChosen()
    {
        Auth.Relabel();
        RelabelAuth();
        foreach (var step in Steps)
        {
            step.RelabelRequest();
        }
        Refresh();
    }

    // What a run saved for steps that were never saved goes. Secrets of saved steps stay, also when the file is gone, as it may come back.
    public Task CloseAsync()
    {
        _closed = true;
        Cancel();
        Auth.CancelFetch();
        foreach (var step in Steps)
        {
            step.Close();
        }
        return _services.ForgetSecretsAsync(_unsavedOwners);
    }

    public void Cancel() => _running?.Cancel();

    // A new step starts with a JSON body and the workflow's auth, as a new tab starts with its folder's.
    public void AddRequest() => Add(new() { Request = new() { BodyKind = BodyKind.Json, Auth = new(AuthKind.Inherit) } });

    public void AddDelay() => Add(new() { DelaySeconds = 5 });

    // The file is made at once with a small start, so it can be opened in an editor. A file that is already there is used as it is.
    public async Task AddScriptAsync()
    {
        var translator = _services.Translator;
        if (_services.Dialogs.AskName(translator.Of("Workflow.ScriptTitle"), "", translator.Of("Folder.Create"),
            name => WorkflowLibrary.IsValidScriptName(ScriptNameOf(name)) ? null : translator.Of("Save.Invalid")) is not { } name)
        {
            return;
        }
        var script = ScriptNameOf(name);
        try
        {
            await _services.Library.CreateScriptAsync(_id, script, translator.Of("Workflow.ScriptTemplate"), CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not create the script {Script} of the workflow {Name}", script, Name);
            _services.Dialogs.Tell(translator.Of("Workflow.ScriptFailed"), translator.DetailsOf(exception));
            return;
        }
        if (!_code.ContainsKey(script))
        {
            await ReadCodeAsync([script]);
        }
        Add(new() { Script = script });
    }

    public string? ScriptPathOf(WorkflowStepViewModel step) => step.Script is { } script && WorkflowLibrary.IsValidScriptName(script) ? _services.Library.ScriptPathOf(_id, script) : null;

    IEnumerable<string> Scripts => Steps.Select(step => step.Script).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // A script that cannot be read has no code here, so a run tells of it instead of running nothing.
    async Task ReadCodeAsync(IEnumerable<string> scripts)
    {
        // An edit made while the files are read is kept.
        foreach (var (script, before) in scripts.Select(script => (script, _code.GetValueOrDefault(script))).ToList())
        {
            var code = await _services.Library.LoadScriptAsync(_id, script, CancellationToken.None);
            if (_code.GetValueOrDefault(script) != before)
            {
                continue;
            }
            if (code is not null)
            {
                _savedCode[script] = code;
                _code[script] = code;
            }
            else
            {
                _savedCode.Remove(script);
                _code.Remove(script);
            }
        }
        OnPropertyChanged(nameof(Code));
        // A script's code tells which values it uses.
        Refresh();
    }

    static string ScriptNameOf(string name) => name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.js";

    void Add(WorkflowStep added)
    {
        var step = Follow(new(added, _services));
        Steps.Add(step);
        SelectedStep = step;
        Edited();
    }

    public void RemoveStep(WorkflowStepViewModel step)
    {
        var index = Steps.IndexOf(step);
        if (index < 0)
        {
            return;
        }
        Drop(step);
        var wasSelected = SelectedStep == step;
        Steps.RemoveAt(index);
        // A script no step runs any more is read from its file again if a step runs it later.
        if (step.Script is { } script && !Scripts.Contains(script, StringComparer.OrdinalIgnoreCase))
        {
            _code.Remove(script);
            _savedCode.Remove(script);
        }
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
        var owners = SecretOwners;
        try
        {
            // The secrets and scripts go first, so workflow.json never points at secrets or code that were not written.
            await SaveSecretsAsync(CancellationToken.None);
            foreach (var (script, code) in _code.Where(pair => _savedCode.GetValueOrDefault(pair.Key) != pair.Value).ToList())
            {
                await _services.Library.SaveScriptAsync(_id, script, code, CancellationToken.None);
                _savedCode[script] = code;
            }
            // A workflow that was deleted on disk is not brought back.
            await _services.Library.SaveAsync(workflow, CancellationToken.None, createDirectory: false);
            _savedJson = JsonOf(workflow);
            // Edits made while the file was written are still unsaved.
            IsDirty = _version != version;
            // Steps that are no longer in the file take their secrets with them.
            var removed = _savedOwners.Union(_unsavedOwners).Except(owners).ToList();
            _savedOwners = owners;
            _unsavedOwners.ExceptWith(owners);
            _unsavedOwners.ExceptWith(removed);
            await _services.ForgetSecretsAsync(removed);
            if (removed.Contains(_id))
            {
                Auth.ForgetSavedSecrets();
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save the workflow {Name}", Name);
            _services.Dialogs.Tell(translator.Of("Workflow.SaveFailed"), translator.DetailsOf(exception));
        }
    }

    // The workflow runs as it is in the editor.
    public async Task RunAsync()
    {
        if (_closed || IsRunning)
        {
            return;
        }
        var translator = _services.Translator;
        Problems = [];
        LastRun = null;
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
        var environment = _services.Environments.SelectedOrNone;
        var steps = Steps.ToList();
        IsRunning = true;
        using var running = _running = new CancellationTokenSource();
        try
        {
            // The run reads the secrets where they are saved, as a tab's send does.
            await SaveSecretsAsync(running.Token);
            // Secrets were all that was unsaved if the workflow itself is unchanged, as in a tab.
            if (IsDirty && !HasUnsavedChanges())
            {
                IsDirty = false;
            }
            WorkflowStepViewModel? last = null;
            var checkedWorkflow = await _services.Check.CheckAsync(workflow, environment, parameters, running.Token, new Dictionary<string, string>(_code, StringComparer.OrdinalIgnoreCase));
            if (checkedWorkflow.Problems.Count > 0)
            {
                Problems = [.. checkedWorkflow.Problems.Select(TextOf)];
                return;
            }
            await _services.Runner.RunAsync(checkedWorkflow, environment, auth => FetchTokenAsync(auth, environment, steps, running.Token), async workflowEvent =>
                {
                    await ShowAsync(workflowEvent, steps, workflow.Steps);
                    if (workflowEvent is StepStarted started)
                    {
                        StepRunning?.Invoke(steps[started.Index]);
                    }
                    if (workflowEvent is StepFinished done)
                    {
                        last = steps[done.Index];
                    }
                    if (workflowEvent is RunFinished finished)
                    {
                        LastRun = finished;
                    }
                },
                running.Token);
            // The last step that ran shows how the run ended, also when it failed and the rest were skipped. A step removed during the run is not chosen.
            if (last is not null && Steps.Contains(last))
            {
                SelectedStep = last;
            }
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
            await ReloadIfPostponedAsync();
        }
    }

    // The run's own steps tell where it saved from, as the steps here can be edited during the run.
    static async Task ShowAsync(WorkflowEvent workflowEvent, IReadOnlyList<WorkflowStepViewModel> steps, IReadOnlyList<WorkflowStep> run)
    {
        switch (workflowEvent)
        {
            case StepStarted started:
                steps[started.Index].Started();
                break;
            case StepFinished finished:
                await steps[finished.Index].FinishedAsync(finished, run[finished.Index].Saves);
                break;
            case StepRetrying retrying:
                steps[retrying.Index].Retrying(retrying.Attempt, run[retrying.Index].Retry?.Times ?? retrying.Attempt);
                break;
            case StepSkipped skipped:
                steps[skipped.Index].Ended(StepOutcome.Skipped);
                break;
            case StepCancelled cancelled:
                steps[cancelled.Index].Ended(StepOutcome.Cancelled);
                break;
        }
    }

    // Only client credentials come here, as they need no login. The token is fetched for the chosen environment and saved with the step, as from a tab.
    // A token that is fetched and saved is no edit, as in a tab, so the workflow is as saved as before unless something else was edited meanwhile.
    // A cancelled fetch cancels the step, as the run was cancelled. A step removed during the run gets no token, as nothing would forget it.
    async Task<bool> FetchTokenAsync(AuthSource auth, ApiEnvironment environment, IReadOnlyList<WorkflowStepViewModel> steps, CancellationToken cancellationToken)
    {
        var owner = auth.SecretsId == _id ? Auth
            : steps.FirstOrDefault(step => step.SecretsId == auth.SecretsId) is { Auth: { } stepAuth } step && Steps.Contains(step) ? stepAuth
            : null;
        if (owner is null)
        {
            return false;
        }
        var (dirty, version) = (IsDirty, _version);
        var fetched = await owner.FetchTokenAsync(environment, saveSecrets: true, cancellationToken);
        if (fetched && _version == version + 1 && !owner.HasUnsavedSecrets)
        {
            IsDirty = dirty;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return fetched;
    }

    // Every request the workflow holds, also one kept in a step of another kind.
    IReadOnlySet<Guid> SecretOwners => WorkflowLibrary.SecretOwnersOf(ToWorkflow()).ToHashSet();

    async Task SaveSecretsAsync(CancellationToken cancellationToken)
    {
        _unsavedOwners.UnionWith(SecretOwners.Except(_savedOwners));
        // Secrets typed for a kind of auth the workflow no longer has would belong to nothing.
        if (Auth.Kind is not (AuthKind.None or AuthKind.Inherit) && Auth.HasUnsavedSecrets)
        {
            await Auth.SaveSecretsAsync(_id, cancellationToken);
        }
        foreach (var step in Steps.ToList())
        {
            await step.SaveSecretsAsync(cancellationToken);
        }
    }

    // Secrets that cannot be read are shown as empty, as in a tab, and the run tells of one that is missing.
    async Task LoadSecretsAsync()
    {
        try
        {
            await Auth.LoadSecretsAsync(_id, CancellationToken.None);
            foreach (var step in Steps.ToList())
            {
                await step.LoadSecretsAsync(CancellationToken.None);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not load the secrets of the workflow {Name}", Name);
        }
        // The names a step uses can be in its secrets.
        Refresh();
    }

    string TextOf(WorkflowProblem problem)
    {
        var translator = _services.Translator;
        var text = problem.Kind switch
        {
            WorkflowProblemKind.InvalidName => translator.Format("WorkflowProblem.InvalidName", problem.Detail),
            WorkflowProblemKind.DuplicateName => translator.Format("WorkflowProblem.DuplicateName", problem.Detail),
            WorkflowProblemKind.UnknownParameter => translator.Format("WorkflowProblem.UnknownParameter", problem.Detail),
            WorkflowProblemKind.MissingParameter => translator.Format("WorkflowProblem.MissingParameter", problem.Detail),
            WorkflowProblemKind.MissingUrl => translator.Of("WorkflowProblem.MissingUrl"),
            WorkflowProblemKind.NotAVariable => translator.Format("WorkflowProblem.NotAVariable", problem.Detail),
            WorkflowProblemKind.InvalidSource => translator.Format("WorkflowProblem.InvalidSource", problem.Detail),
            WorkflowProblemKind.UsedBeforeSaved => translator.Format("WorkflowProblem.UsedBeforeSaved", problem.Detail),
            WorkflowProblemKind.ScriptNotFound => translator.Format("WorkflowProblem.ScriptNotFound", problem.Detail),
            WorkflowProblemKind.InvalidScript => translator.Format("WorkflowProblem.InvalidScript", problem.Detail),
            WorkflowProblemKind.MixedStep => translator.Of("WorkflowProblem.MixedStep"),
            WorkflowProblemKind.InvalidDelay => translator.Format("WorkflowProblem.InvalidDelay", WorkflowCheck.MaxDelaySeconds),
            WorkflowProblemKind.InvalidRetry => translator.Format("WorkflowProblem.InvalidRetry", WorkflowCheck.MaxRetryTimes, WorkflowCheck.MaxDelaySeconds),
            WorkflowProblemKind.InvalidOutput => translator.Of("WorkflowProblem.InvalidOutput"),
            _ => translator.Format("WorkflowProblem.UnknownName", problem.Detail),
        };
        return problem.Step is { } step ? translator.Format("Workflow.StepProblem", step + 1, text) : text;
    }

    // The variables are the names the steps save into, and each step shows which of the workflow's own names it uses and which it saves.
    // A name that is a parameter is no variable, so the check tells of a step that saves into it.
    void Refresh()
    {
        var parameters = Parameters.ToList().Select(parameter => parameter.Name).ToHashSet();
        foreach (var (index, step) in Steps.Index())
        {
            step.Number = index + 1;
            step.IsLast = index == Steps.Count - 1;
            step.SavedNames = [.. step.Saves.ToList().Select(save => save.Name).Where(WorkflowCheck.IsValidName).Distinct()];
        }
        _variables = [.. Steps.SelectMany(step => step.SavedNames).Where(name => !parameters.Contains(name)).Distinct()];
        var declared = parameters.Concat(_variables).ToHashSet();
        foreach (var step in Steps)
        {
            var used = step.Kind switch
            {
                StepKind.Script => WorkflowCheck.VarsIn(_code.GetValueOrDefault(step.Script!, "")).Distinct(),
                // A step that inherits uses the names in the workflow's auth too.
                _ when step.Auth?.Kind == AuthKind.Inherit => step.Used.Union(Auth.Texts.SelectMany(WorkflowCheck.NamesIn)),
                _ => step.Used,
            };
            step.UsedNames = [.. used.Where(declared.Contains).Order(StringComparer.Ordinal)];
        }
    }

    void Load(Workflow workflow)
    {
        Rename(workflow.Name);
        _savedOwners = WorkflowLibrary.SecretOwnersOf(workflow).ToHashSet();
        _variableDefaults = workflow.Variables.Where(variable => variable.HasDefault).DistinctBy(variable => variable.Name).ToDictionary(variable => variable.Name, variable => variable.Default);
        _savedJson = JsonOf(workflow);
        Auth.UseOwner(workflow.Id);
        Auth.Load(workflow.Auth ?? AuthSettings.None);
        _code.Clear();
        _savedCode.Clear();
        Parameters.Load(workflow.Parameters.Select(EntryOf));
        foreach (var step in Steps)
        {
            Drop(step);
        }
        Steps.Clear();
        foreach (var step in workflow.Steps)
        {
            Steps.Add(Follow(new(step, _services)));
        }
        SelectedStep = Steps.FirstOrDefault();
        Problems = [];
        LastRun = null;
        IsDirty = false;
        Refresh();
    }

    // As the button beside the auth in a tab, a token fetched here is saved as "Hent token" saves it.
    public Task<bool> RefreshAuthAsync() => FetchByHandAsync(Auth);

    // A step that inherits fetches the workflow's token.
    public Task<bool> RefreshAuthAsync(WorkflowStepViewModel step) => step.EffectiveAuth is { } auth ? FetchByHandAsync(auth) : Task.FromResult(false);

    void RelabelAuth()
    {
        OnPropertyChanged(nameof(HasOAuth));
        OnPropertyChanged(nameof(IsAuthRefreshing));
        OnPropertyChanged(nameof(CanRefreshAuth));
        OnPropertyChanged(nameof(RefreshAuthTip));
        foreach (var step in Steps)
        {
            step.RelabelAuth();
        }
    }

    WorkflowStepViewModel Follow(WorkflowStepViewModel step)
    {
        step.Changed += Edited;
        step.Inherit(Auth, () => Name);
        if (step.Auth is { } auth)
        {
            auth.OwnerFetch = () => FetchByHandAsync(auth);
        }
        return step;
    }

    // A token fetched by hand is saved with the secrets, as a run does, so it is no edit.
    async Task<bool> FetchByHandAsync(AuthViewModel auth)
    {
        _fetchesByHand++;
        try
        {
            return await FetchAndSaveAsync(auth);
        }
        finally
        {
            _fetchesByHand--;
            await ReloadIfPostponedAsync();
        }
    }

    async Task ReloadIfPostponedAsync()
    {
        if (_reloadPostponed && !IsRunning && _fetchesByHand == 0 && !_closed)
        {
            _reloadPostponed = false;
            await ReloadAsync();
        }
    }

    async Task<bool> FetchAndSaveAsync(AuthViewModel auth)
    {
        if (!await auth.FetchTokenAsync() || _closed)
        {
            return false;
        }
        try
        {
            await SaveSecretsAsync(CancellationToken.None);
            if (IsDirty && !HasUnsavedChanges())
            {
                IsDirty = false;
            }
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            _services.Logger.LogError(exception, "Could not save the secrets of the workflow {Name}", Name);
            _services.Dialogs.Tell(_services.Translator.Of("Workflow.SecretsSaveFailed"), _services.Translator.DetailsOf(exception));
            return false;
        }
    }

    // A step that is gone must not go on fetching a token.
    void Drop(WorkflowStepViewModel step)
    {
        step.Changed -= Edited;
        step.Close();
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
        Name = Name,
        Parameters = ValuesOf(Parameters),
        Variables = [.. _variables.Select(name => new WorkflowValue(name) { Default = _variableDefaults.GetValueOrDefault(name) })],
        Steps = [.. Steps.Select(step => step.ToStep())],
        Auth = Auth.ToSettings() is { Kind: not (AuthKind.None or AuthKind.Inherit) } auth ? auth : null,
    };

    bool HasUnsavedChanges() => JsonOf(ToWorkflow()) != _savedJson || _code.Any(pair => _savedCode.GetValueOrDefault(pair.Key) != pair.Value) || Auth.HasUnsavedSecrets || Steps.Any(step => step.Auth?.HasUnsavedSecrets == true);

    string? InvalidDefault() => Parameters.ToList().FirstOrDefault(value => !TryDefaultOf(value.Value, out _))?.Name;

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

    // A rename is saved at once, so the name is no change to save or to reload.
    static string JsonOf(Workflow workflow) => JsonSerializer.Serialize(workflow with { Name = "" }, CompactJson.Options);
}
