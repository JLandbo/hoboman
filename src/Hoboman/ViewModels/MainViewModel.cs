using System.Collections.ObjectModel;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class MainViewModel(
    RequestTreeViewModel tree,
    HistoryViewModel history,
    WorkflowsViewModel workflows,
    WorkflowServices workflowServices,
    EnvironmentsViewModel environments,
    SettingsViewModel settings,
    SettingsStore settingsStore,
    EnvironmentEditorViewModel environmentEditor,
    FolderAuthViewModel folderAuth,
    RequestTabServices tabServices,
    RequestLibrary library,
    RequestDeletion deletion,
    IDialogs dialogs,
    ClipboardViewModel clipboard,
    Translator translator,
    ILogger<MainViewModel> logger) : ObservableObject
{
    readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase);
    int _lastNumber;

    Coalescer RequestsReload => field ??= new(() => tabServices.CollectionChanges.RunAsync(ReloadRequestsAsync));

    Coalescer HistoryReload => field ??= new(() => history.RefreshAsync(CancellationToken.None));

    Coalescer EnvironmentsReload => field ??= new(() => environments.LoadAsync(CancellationToken.None));

    Coalescer WorkflowsReload => field ??= new(ReloadWorkflowsAsync);

    public RequestTreeViewModel Tree => tree;

    public HistoryViewModel History => history;

    public EnvironmentsViewModel Environments => environments;

    public WorkflowsViewModel Workflows => workflows;

    public ClipboardViewModel Clipboard => clipboard;

    public ObservableCollection<RequestTabViewModel> Tabs { get; } = tree.Follow([]);

    public TabSession Session => new([.. Tabs.Where(tab => tab.Name is { } name && library.Exists(name)).Select(tab => tree.KeyOf(tab.Name!))], SelectedTab?.Name is { } selected ? tree.KeyOf(selected) : null);

    public bool IsChangingCollection => tabServices.CollectionChanges.IsRunning;

    public void MoveTab(RequestTabViewModel tab, RequestTabViewModel? target, bool after)
    {
        if (!Tabs.Contains(tab) || tab == target || target is not null && !Tabs.Contains(target))
        {
            return;
        }
        var remaining = Tabs.Where(item => item != tab).ToList();
        var index = target is null ? remaining.Count : remaining.IndexOf(target) + (after ? 1 : 0);
        var selected = SelectedTab;
        Tabs.Move(Tabs.IndexOf(tab), index);
        SelectedTab = selected;
    }

    public RequestTabViewModel? SelectedTab
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                tree.Activate(value);
                OnPropertyChanged(nameof(Content));
            }
        }
    }

    public WorkflowViewModel? Workflow
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Content));
            }
        }
    }

    // The sidebar chooses what the main area shows: the request tabs for the collections and the history, and the open workflow, without the tabs, for the workflows.
    public SidebarSection Section
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(Content));
            }
        }
    }

    public object? Content => Section == SidebarSection.Workflows ? Workflow : SelectedTab;

    public async Task LoadAsync()
    {
        await tabServices.CollectionChanges.RunAsync(RetrySecretCleanupAsync);
        await RequestsChangedAsync();
        await HistoryChangedAsync();
        await EnvironmentsChangedAsync();
        await WorkflowsChangedAsync();
        try
        {
            if ((await settingsStore.LoadAsync(CancellationToken.None)).Session is { } session)
            {
                foreach (var name in session.Requests.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(node => !node.IsFolder && (SameName(node.OrderKey, name) || SameName(node.Path, name))) is { } node)
                    {
                        await OpenAsync(node);
                    }
                }
                SelectedTab = Tabs.FirstOrDefault(tab => tab.Name is { } name && (SameName(tree.KeyOf(name), session.Selected) || SameName(name, session.Selected))) ?? SelectedTab;
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not restore the open request tabs");
        }
        if (Tabs.Count == 0)
        {
            NewTab();
        }
    }

    public Task RequestsChangedAsync() => RequestsReload.RunAsync();

    public Task HistoryChangedAsync() => HistoryReload.RunAsync();

    public Task EnvironmentsChangedAsync() => EnvironmentsReload.RunAsync();

    public Task WorkflowsChangedAsync() => WorkflowsReload.RunAsync();

    public Task LanguageChangedAsync()
    {
        history.Relabel();
        RelabelTabs();
        Workflow?.Relabel();
        return HistoryChangedAsync();
    }

    // Each environment has its own OAuth tokens, so the tabs show the chosen environment's, and the workflow shows which names it has.
    public void EnvironmentChosen()
    {
        RelabelTabs();
        Workflow?.Relabel();
    }

    public void NewTab()
    {
        logger.LogDebug("Opened a new tab");
        Add(new(tabServices, ApiRequest.New()) { Number = ++_lastNumber });
    }

    public async Task OpenAsync(RequestNodeViewModel node)
    {
        if ((node.IsDraft ? node.Tab : TabOf(node.Path)) is { } open)
        {
            open.RequestSection = RequestSection.Body;
            Show(open);
            return;
        }
        if (!_opening.Add(node.Path))
        {
            return;
        }
        try
        {
            if (await library.LoadAsync(node.Path, CancellationToken.None) is not { } request)
            {
                await tree.LoadAsync(CancellationToken.None);
                return;
            }
            var tab = new RequestTabViewModel(tabServices, request, name: node.Path);
            Add(tab);
            await tab.LoadSecretsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not open {Name}", node.Path);
            dialogs.Tell(translator.Of("Open.Failed"), translator.DetailsOf(exception));
        }
        finally
        {
            _opening.Remove(node.Path);
        }
    }

    public async Task OpenAsync(HistoryItemViewModel item)
    {
        if (Tabs.FirstOrDefault(tab => tab.HistoryName == item.File.Name) is { } open)
        {
            open.RequestSection = RequestSection.Body;
            Show(open);
            return;
        }
        logger.LogInformation("Opened the call to {Address} from the history", item.Address);
        var entry = item.File.Entry;
        var tab = new RequestTabViewModel(tabServices, entry.Request, suggestedName: entry.Name is not null ? tree.PathOf(entry.Request.Id) ?? entry.Name : null, historyName: item.File.Name) { Number = entry.Name is null ? ++_lastNumber : 0 };
        if (Tabs.FirstOrDefault(open => open.IsPreview) is { } preview)
        {
            preview.Close();
            Tabs[Tabs.IndexOf(preview)] = tab;
        }
        else
        {
            Tabs.Add(tab);
        }
        Show(tab);
        await tab.ShowAsync(entry);
        await tab.LoadSecretsAsync(CancellationToken.None);
    }

    public void Close(RequestTabViewModel tab)
    {
        if (tab.IsDirty)
        {
            if (!dialogs.Confirm(translator.Of("Close.TabTitle"), translator.Format("Close.TabMessage", tab.Title), translator.Of("Close.Confirm"), []))
            {
                return;
            }
            logger.LogInformation("Closed {Title} without saving it", tab.Title);
        }
        CloseTab(tab);
    }

    void CloseTab(RequestTabViewModel tab)
    {
        if (!Tabs.Contains(tab))
        {
            return;
        }
        tab.Close();
        var index = Tabs.IndexOf(tab);
        var wasSelected = SelectedTab == tab;
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            // With nothing open the numbers start over, so closing the last request gives a fresh one instead of the next number.
            _lastNumber = 0;
            NewTab();
        }
        else if (wasSelected)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }

    // The app ends right after, so what a run saved for steps that were never saved is forgotten here and waited for.
    // The forgetting does not come back to this thread, so waiting cannot lock up.
    public void Exit() => Workflow?.CloseAsync().GetAwaiter().GetResult();

    public bool CanClose()
    {
        if (IsChangingCollection)
        {
            dialogs.Tell(translator.Of("Close.BusyTitle"), translator.Of("Close.BusyMessage"));
            return false;
        }
        var unsaved = Tabs.Where(tab => tab.IsDirty).Select(tab => tab.Title).ToList();
        if (Workflow is { IsDirty: true } workflow)
        {
            unsaved.Add(workflow.Name);
        }
        if (unsaved.Count == 0)
        {
            return true;
        }
        var close = dialogs.Confirm(translator.Of("Close.Title"), translator.Of("Close.Message"), translator.Of("Close.Confirm"), unsaved);
        logger.LogInformation(close ? "Closing with {Count} unsaved requests" : "Kept the app open for {Count} unsaved requests", unsaved.Count);
        return close;
    }

    public async Task NewFolderAsync()
    {
        if (dialogs.AskName(translator.Of("Folder.Title"), "", translator.Of("Folder.Create"), name => name.Contains('/') ? translator.Of("Save.Invalid") : ProblemOfFolder(name)) is not { } name)
        {
            return;
        }
        await CreateFolderAsync(name);
    }

    public async Task NewDraftAsync(RequestNodeViewModel folder)
    {
        var tab = new RequestTabViewModel(tabServices, ApiRequest.New(), destination: folder.Path) { Number = ++_lastNumber };
        Add(tab);
        await tab.UpdateAuthSourceAsync();
    }

    public async Task NewSubfolderAsync(RequestNodeViewModel parent)
    {
        if (dialogs.AskName(translator.Format("Folder.TitleIn", parent.Path), "", translator.Of("Folder.Create"), name => name.Contains('/') ? translator.Of("Save.Invalid") : ProblemOfNewFolder($"{parent.Path}/{name.Trim()}")) is not { } name)
        {
            return;
        }
        var path = $"{parent.Path}/{name.Trim()}";
        if (await CreateFolderAsync(path))
        {
            tree.ExpandTo(path);
        }
    }

    async Task<bool> CreateFolderAsync(string name)
    {
        try
        {
            await library.CreateFolderAsync(name, CancellationToken.None);
            await tree.LoadAsync(CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not create the folder {Name}", name);
            dialogs.Tell(translator.Of("Folder.Failed"), translator.DetailsOf(exception));
            return false;
        }
    }

    public async Task RenameAsync(RequestNodeViewModel node)
    {
        string FullName(string value) => RequestLibrary.ParentOf(node.Path) is { } parent ? $"{parent}/{value}" : value;
        if (dialogs.AskName(translator.Of("Rename.Title"), node.Name, translator.Of("Common.Save"), candidate => candidate.Contains('/') ? translator.Of("Save.Invalid") : SameName(candidate, node.Name) ? null : tabServices.ProblemOfName(FullName(candidate))) is not { } name || name == node.Name)
        {
            return;
        }
        await ChangeCollectionAsync(() => RenameToAsync(node, FullName(name), translator.Of("Rename.Failed")));
    }

    public async Task RenameTabAsync(RequestTabViewModel tab)
    {
        string FullName(string value) => (RequestLibrary.ParentOf(tab.Name ?? tab.SuggestedName) ?? tab.Destination) is { } parent ? $"{parent}/{value}" : value;
        if (dialogs.AskName(translator.Of("Rename.Title"), tab.Title, translator.Of("Common.Save"), value => value.Contains('/') ? translator.Of("Save.Invalid") : SameName(FullName(value), tab.Name) ? null : tabServices.ProblemOfName(FullName(value))) is not { } name)
        {
            return;
        }
        await ChangeCollectionAsync(async () =>
        {
            if (!Tabs.Contains(tab))
            {
                return;
            }
            if (tab.Name is not { } current)
            {
                await tab.SaveCoreAsync(FullName(name));
                return;
            }
            if (FullName(name) != current)
            {
                await RenameToAsync(new(current, tab.Editor.Method, false), FullName(name), translator.Of("Rename.Failed"));
            }
        });
    }

    public Task CloneAsync(RequestNodeViewModel node) => ChangeCollectionAsync(() => CloneCoreAsync(node));

    async Task CloneCoreAsync(RequestNodeViewModel node)
    {
        if (node.IsFolder)
        {
            return;
        }
        try
        {
            var open = node.Tab ?? TabOf(node.Path);
            var source = open?.ToRequest() ?? await library.LoadAsync(node.Path, CancellationToken.None);
            if (source is null)
            {
                return;
            }
            var parent = node.IsDraft ? open!.Destination : RequestLibrary.ParentOf(node.Path);
            var stem = node.IsDraft ? open!.Title : node.Name;
            var number = 1;
            var suffix = stem.LastIndexOf(" (", StringComparison.Ordinal);
            if (suffix >= 0 && stem.EndsWith(')') && int.TryParse(stem.AsSpan(suffix + 2, stem.Length - suffix - 3), out var previous) && previous is >= 0 and < int.MaxValue)
            {
                stem = stem[..suffix];
                number = previous + 1;
            }
            string name;
            do
            {
                var leaf = $"{stem} ({number++})";
                name = parent is null ? leaf : $"{parent}/{leaf}";
            } while (library.Exists(name) || Tabs.Any(tab => SameName(tab.DraftName, name)));
            var clone = source with { Id = Guid.NewGuid() };
            try
            {
                if (open is not null)
                {
                    await open.Auth.CopySecretsAsync(clone.Id, CancellationToken.None);
                }
                else
                {
                    await tabServices.AuthRefresh.SaveAsync(() => tabServices.Secrets.CopyAsync(source.Id, clone.Id, CancellationToken.None), CancellationToken.None);
                }
                await library.CreateAsync(name, clone, CancellationToken.None);
            }
            catch
            {
                await tabServices.Secrets.DeleteAsync(clone.Id, CancellationToken.None);
                throw;
            }
            await tree.LoadAsync(CancellationToken.None);
            await tree.PlaceAsync(tree.KeyOf(name), parent, node.OrderKey, DropPosition.After);
            await OpenAsync(new RequestNodeViewModel(name, clone.Method, false));
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not clone {Name}", node.Path);
            dialogs.Tell(translator.Of("Clone.Failed"), translator.DetailsOf(exception));
        }
    }

    // A request dropped on another request goes into the folder that one is in, and one dropped beside the folders goes to the top.
    public bool CanMove(RequestNodeViewModel node, RequestNodeViewModel? target, DropPosition position)
    {
        var folder = DestinationOf(target, position);
        var name = folder is null ? node.Name : $"{folder}/{node.Name}";
        return node != target && (target is null || !SameName(node.OrderKey, target.OrderKey))
            && (!node.IsFolder || !SameName(folder, node.Path) && folder?.StartsWith($"{node.Path}/", StringComparison.OrdinalIgnoreCase) != true)
            && (folder is null || library.FolderExists(folder))
            && (node.IsDraft || SameName(name, node.Path) || (node.IsFolder ? !library.FolderExists(name) : !library.Exists(name)));
    }

    static string? DestinationOf(RequestNodeViewModel? target, DropPosition position) => target is null ? null : position == DropPosition.Inside && target.IsFolder ? target.Path : RequestLibrary.ParentOf(target.Path);

    public Task MoveAsync(RequestNodeViewModel node, RequestNodeViewModel? target, DropPosition position = DropPosition.Inside) => ChangeCollectionAsync(() => MoveCoreAsync(node, target, position));

    async Task MoveCoreAsync(RequestNodeViewModel node, RequestNodeViewModel? target, DropPosition position)
    {
        var current = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(row => SameName(row.OrderKey, node.OrderKey));
        if (current is null)
        {
            return;
        }
        node = current;
        if (!CanMove(node, target, position))
        {
            if (node != target)
            {
                dialogs.Tell(translator.Of("Move.Failed"), translator.Of("Move.Invalid"));
            }
            return;
        }
        var folder = DestinationOf(target, position);
        var name = folder is null ? node.Name : $"{folder}/{node.Name}";
        try
        {
            if (node.IsDraft)
            {
                node.Tab!.MoveTo(folder);
                tree.RefreshDrafts();
            }
            else if (!SameName(name, node.Path) && !(node.IsFolder ? await RenameFolderToAsync(node, name) : await RenameToAsync(node, name, translator.Of("Move.Failed"))))
            {
                return;
            }
            await tree.PlaceAsync(node.IsDraft ? node.OrderKey : node.IsFolder ? $"{name}/" : tree.KeyOf(name), folder, position == DropPosition.Inside ? null : target?.OrderKey, position);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not move {Name}", node.Path);
            dialogs.Tell(translator.Of("Move.Failed"), translator.DetailsOf(exception));
        }
    }

    async Task<bool> RenameToAsync(RequestNodeViewModel node, string name, string failed)
    {
        try
        {
            await library.RenameAsync(node.Path, name, CancellationToken.None);
            TabOf(node.Path)?.Rename(name);
            await tree.RenamedAsync(node, name);
            await tree.LoadAsync(CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename {Name} to {NewName}", node.Path, name);
            dialogs.Tell(failed, translator.DetailsOf(exception));
            await tree.LoadAsync(CancellationToken.None);
            return false;
        }
    }

    public async Task RenameFolderAsync(RequestNodeViewModel folder)
    {
        string FullName(string value) => RequestLibrary.ParentOf(folder.Path) is { } parent ? $"{parent}/{value}" : value;
        if (dialogs.AskName(translator.Of("RenameFolder.Title"), folder.Name, translator.Of("Common.Save"), candidate => candidate.Contains('/') ? translator.Of("Save.Invalid") : SameName(candidate, folder.Name) ? null : ProblemOfNewFolder(FullName(candidate))) is not { } name
            || name == folder.Name)
        {
            return;
        }
        await ChangeCollectionAsync(() => RenameFolderToAsync(folder, FullName(name)));
    }

    async Task<bool> RenameFolderToAsync(RequestNodeViewModel folder, string name)
    {
        folder = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(node => node.IsFolder && SameName(node.Path, folder.Path)) ?? folder;
        var inside = RequestTreeViewModel.Flatten([folder]).ToList();
        // The tree keeps folders open by their path, so the open ones are opened again under the new one,
        // and the folders it now lies in are opened too, as it would otherwise seem to vanish.
        var opened = inside.Where(node => node.IsFolder && node.IsExpanded).Select(node => Moved(node.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var parent = RequestLibrary.ParentOf(name); parent is not null; parent = RequestLibrary.ParentOf(parent))
        {
            opened.Add(parent);
        }
        try
        {
            await tabServices.AuthRefresh.SaveAsync(() => library.RenameFolderAsync(folder.Path, name, CancellationToken.None), CancellationToken.None);
            foreach (var request in inside.Where(node => !node.IsFolder && !node.IsDraft))
            {
                TabOf(request.Path)?.Rename(Moved(request.Path));
            }
            foreach (var draft in inside.Where(node => node.IsDraft))
            {
                draft.Tab!.MoveTo(Moved(draft.Tab.Destination!));
            }
            await tree.RenamedAsync(folder, name);
            await tree.LoadAsync(CancellationToken.None);
            foreach (var node in RequestTreeViewModel.Flatten(tree.Nodes).Where(node => opened.Contains(node.Path)))
            {
                node.IsExpanded = true;
            }
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename the folder {Name} to {NewName}", folder.Path, name);
            dialogs.Tell(translator.Of("RenameFolder.Failed"), translator.DetailsOf(exception));
            await tree.LoadAsync(CancellationToken.None);
            return false;
        }

        string Moved(string path) => $"{name}{path[folder.Path.Length..]}";
    }

    public Task DeleteAsync(RequestNodeViewModel node) => ChangeCollectionAsync(() => DeleteCoreAsync(node));

    async Task DeleteCoreAsync(RequestNodeViewModel node)
    {
        if (!dialogs.Confirm(translator.Of("Delete.Title"), translator.Format("Delete.Message", node.Name), translator.Of("Delete.Confirm"), []))
        {
            return;
        }
        try
        {
            await DeleteFromLibraryAsync(node);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete {Name}", node.Path);
            dialogs.Tell(translator.Of("Delete.Failed"), translator.DetailsOf(exception));
        }
    }

    public Task DeleteFolderAsync(RequestNodeViewModel folder) => ChangeCollectionAsync(() => DeleteFolderCoreAsync(folder));

    async Task DeleteFolderCoreAsync(RequestNodeViewModel folder)
    {
        folder = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(node => node.IsFolder && SameName(node.Path, folder.Path)) ?? folder;
        var inside = RequestTreeViewModel.Flatten([folder]).ToList();
        var requests = inside.Where(node => !node.IsFolder && !node.IsDraft).Select(node => node.Path).ToList();
        var drafts = inside.Where(node => node.IsDraft).Select(node => node.Tab!).ToList();
        var unsaved = Tabs.Where(tab => drafts.Contains(tab) || tab.IsDirty && requests.Contains(tab.Name, StringComparer.OrdinalIgnoreCase)).Select(tab => tab.Title).ToList();
        if (!dialogs.Confirm(translator.Of("DeleteFolder.Title"), translator.Format("DeleteFolder.Message", folder.Name, requests.Count), translator.Of("Delete.Confirm"), unsaved))
        {
            return;
        }
        try
        {
            await DeleteFromLibraryAsync(folder);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete the folder {Name}", folder.Path);
            dialogs.Tell(translator.Of("DeleteFolder.Failed"), translator.DetailsOf(exception));
        }
    }

    async Task DeleteFromLibraryAsync(RequestNodeViewModel node)
    {
        try
        {
            await tabServices.AuthRefresh.SaveAsync(() => DeleteAndCloseAsync(node), CancellationToken.None);
        }
        finally
        {
            await tree.LoadAsync(CancellationToken.None);
        }
        await tree.SaveOrderAsync();
    }

    async Task DeleteAndCloseAsync(RequestNodeViewModel node)
    {
        node = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(row => row.IsFolder == node.IsFolder && SameName(row.Path, node.Path)) ?? node;
        var inside = RequestTreeViewModel.Flatten([node]).ToList();
        var requests = inside.Where(row => !row.IsFolder && !row.IsDraft).ToDictionary(row => row.Path, row => tree.IdOf(row.Path));
        var drafts = inside.Where(row => row.IsDraft).Select(row => row.Tab!).ToList();
        try
        {
            await deletion.DeleteAsync(node.Path, node.IsFolder, CancellationToken.None);
        }
        finally
        {
            var removed = requests.Where(request => !library.Exists(request.Key)).ToList();
            foreach (var (name, _) in removed)
            {
                if (TabOf(name) is { } tab)
                {
                    CloseTab(tab);
                }
            }
            foreach (var tab in Tabs.Where(tab => !tab.OwnsId && removed.Any(request => request.Value == tab.Id)))
            {
                tab.Auth.ForgetSavedSecrets();
            }
            foreach (var draft in drafts.Where(tab => tab.Destination is { } destination && !library.FolderExists(destination)))
            {
                CloseTab(draft);
            }
        }
    }

    async Task RetrySecretCleanupAsync()
    {
        try
        {
            await tabServices.AuthRefresh.SaveAsync(() => deletion.CleanupAsync(CancellationToken.None), CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not finish the pending secret cleanup");
            dialogs.Tell(translator.Of("Secrets.CleanupFailed"), translator.DetailsOf(exception));
        }
    }

    public async Task DeleteHistoryAsync(HistoryItemViewModel item)
    {
        try
        {
            await history.DeleteAsync(item, CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete the call {Name} from the history", item.File.Name);
            dialogs.Tell(translator.Of("History.DeleteFailed"), translator.DetailsOf(exception));
        }
    }

    public async Task OpenWorkflowAsync(string name)
    {
        if (Workflow is { } open && SameName(open.Name, name))
        {
            Section = SidebarSection.Workflows;
            return;
        }
        if (Workflow is { IsRunning: true } running && !dialogs.Confirm(translator.Of("Workflow.StopTitle"), translator.Format("Workflow.StopMessage", running.Name), translator.Of("Workflow.Stop"), []))
        {
            return;
        }
        if (Workflow is { IsDirty: true } unsaved && !dialogs.Confirm(translator.Of("Workflow.CloseTitle"), translator.Format("Workflow.CloseMessage", unsaved.Name), translator.Of("Close.Confirm"), []))
        {
            return;
        }
        var workflow = new WorkflowViewModel(workflowServices, name);
        if (!await workflow.LoadAsync())
        {
            await WorkflowsChangedAsync();
            return;
        }
        logger.LogInformation("Opened the workflow {Name}", name);
        var closing = Workflow?.CloseAsync() ?? Task.CompletedTask;
        Workflow = workflow;
        Section = SidebarSection.Workflows;
        await closing;
    }

    public async Task NewWorkflowAsync()
    {
        if (dialogs.AskName(translator.Of("Sidebar.NewWorkflow"), "", translator.Of("Folder.Create"), ProblemOfWorkflow) is not { } name)
        {
            return;
        }
        try
        {
            await workflowServices.Library.CreateAsync(name, CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not create the workflow {Name}", name);
            dialogs.Tell(translator.Of("Workflow.CreateFailed"), translator.DetailsOf(exception));
            return;
        }
        await WorkflowsChangedAsync();
        await OpenWorkflowAsync(name);
    }

    public async Task RenameWorkflowAsync(string name)
    {
        if (dialogs.AskName(translator.Of("Workflow.RenameTitle"), name, translator.Of("Common.Save"), candidate => SameName(candidate, name) ? null : ProblemOfWorkflow(candidate)) is not { } newName || newName == name)
        {
            return;
        }
        try
        {
            await workflowServices.Library.RenameAsync(name, newName, CancellationToken.None);
            if (Workflow is { } open && SameName(open.Name, name))
            {
                open.Rename(newName);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename the workflow {Name} to {NewName}", name, newName);
            dialogs.Tell(translator.Of("Workflow.RenameFailed"), translator.DetailsOf(exception));
        }
        await WorkflowsChangedAsync();
    }

    public async Task DeleteWorkflowAsync(string name)
    {
        if (!dialogs.Confirm(translator.Of("Workflow.DeleteTitle"), translator.Format("Delete.Message", name), translator.Of("Delete.Confirm"), []))
        {
            return;
        }
        try
        {
            // The steps' secrets go with the workflow, but only when it can be read, so they are known.
            var owners = await StepIdsOfAsync(name);
            await workflowServices.Library.DeleteAsync(name, CancellationToken.None);
            if (Workflow is { } open && SameName(open.Name, name))
            {
                await CloseWorkflowAsync();
            }
            await workflowServices.ForgetSecretsAsync(owners);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete the workflow {Name}", name);
            dialogs.Tell(translator.Of("Workflow.DeleteFailed"), translator.DetailsOf(exception));
        }
        await WorkflowsChangedAsync();
    }

    async Task<IReadOnlyList<Guid>> StepIdsOfAsync(string name)
    {
        try
        {
            return await workflowServices.Library.LoadAsync(name, CancellationToken.None) is { } workflow ? [.. WorkflowLibrary.SecretOwnersOf(workflow)] : [];
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the workflow {Name}, so its secrets are kept", name);
            return [];
        }
    }

    async Task ReloadWorkflowsAsync()
    {
        await workflows.LoadAsync(CancellationToken.None);
        // A workflow that is gone on disk stays open while it has edits, so they are not lost.
        if (Workflow is { } open && !await open.ReloadAsync() && !open.IsDirty)
        {
            logger.LogInformation("The workflow {Name} was removed on disk", open.Name);
            await CloseWorkflowAsync();
        }
    }

    async Task CloseWorkflowAsync()
    {
        var closing = Workflow?.CloseAsync() ?? Task.CompletedTask;
        Workflow = null;
        await closing;
    }

    string? ProblemOfWorkflow(string name) =>
        !WorkflowLibrary.IsValidName(name) ? translator.Of("Save.Invalid")
        : workflows.Contains(name) ? translator.Of("Workflow.Exists")
        : null;

    public async Task EditSettingsAsync()
    {
        await settings.LoadAsync(CancellationToken.None);
        dialogs.EditSettings(settings);
    }

    public async Task EditEnvironmentsAsync()
    {
        await environmentEditor.LoadAsync(CancellationToken.None);
        dialogs.EditEnvironments(environmentEditor);
    }

    public async Task EditFolderAuthAsync(RequestNodeViewModel folder)
    {
        await folderAuth.LoadAsync(folder.Path, CancellationToken.None);
        dialogs.EditFolderAuth(folderAuth);
    }

    async Task ReloadRequestsAsync()
    {
        await tree.LoadAsync(CancellationToken.None);
        foreach (var tab in Tabs.ToList())
        {
            try
            {
                if ((tab.Name is null && !tab.OwnsId) || !await FollowAsync(tab))
                {
                    await tab.UpdateAuthSourceAsync();
                }
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Could not reload {Name}", tab.Name);
                tab.ShowFileProblem(translator.DetailsOf(exception));
            }
        }
    }

    async Task ChangeCollectionAsync(Func<Task> change)
    {
        try
        {
            await tabServices.CollectionChanges.RunAsync(async () =>
            {
                tree.ResetOrderFailure();
                await change();
            });
        }
        finally
        {
            await RequestsChangedAsync();
        }
    }

    // The file wins over the tab, and a tab whose file was moved or renamed finds it again by its id.
    async Task<bool> FollowAsync(RequestTabViewModel tab)
    {
        var name = tab.Name;
        var request = name is null ? null : await library.LoadAsync(name, CancellationToken.None);
        // A file that moved onto a name another tab already has is left to that tab.
        if (request is null && tree.NameOf(tab.Id) is { } moved && TabOf(moved) is null)
        {
            name = moved;
            request = await library.LoadAsync(moved, CancellationToken.None);
        }
        if (request is null)
        {
            if (tab.Name is not null)
            {
                logger.LogInformation("{Name} was removed on disk", tab.Name);
                tab.Unlink();
            }
            return false;
        }
        if (!SameName(name, tab.Name))
        {
            logger.LogInformation("{Name} was moved to {NewName} on disk", tab.Name, name);
            tab.Rename(name!);
        }
        if (tab.ReloadIfChanged(request))
        {
            logger.LogInformation("{Name} changed on disk and was reloaded", name);
            await tab.LoadSecretsAsync(CancellationToken.None);
            return true;
        }
        return false;
    }

    void RelabelTabs()
    {
        foreach (var tab in Tabs)
        {
            tab.Relabel();
        }
    }

    void Add(RequestTabViewModel tab)
    {
        Tabs.Add(tab);
        Show(tab);
    }

    // A tab that is opened is shown, so the main area leaves the workflows for the requests.
    void Show(RequestTabViewModel tab)
    {
        SelectedTab = tab;
        if (Section == SidebarSection.Workflows)
        {
            Section = SidebarSection.Collections;
        }
    }

    RequestTabViewModel? TabOf(string name) => Tabs.FirstOrDefault(tab => SameName(tab.Name, name));

    string? ProblemOfFolder(string name) => RequestLibrary.IsValidName(name) ? null : translator.Of("Save.Invalid");

    string? ProblemOfNewFolder(string name) => ProblemOfFolder(name) ?? (library.FolderExists(name) ? translator.Of("Folder.Exists") : null);

    // Windows does not tell upper and lower case apart in file names.
    static bool SameName(string? name, string? other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
}
