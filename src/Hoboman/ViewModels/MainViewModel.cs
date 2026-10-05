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
    CredentialsViewModel credentials,
    FolderAuthViewModel folderAuth,
    RequestTabServices tabServices,
    RequestLibrary library,
    RequestDeletion deletion,
    IDialogs dialogs,
    ClipboardViewModel clipboard,
    Translator translator,
    ILogger<MainViewModel> logger) : ObservableObject
{
    readonly HashSet<Guid> _opening = [];
    int _lastNumber;

    Coalescer RequestsReload => field ??= new(() => tabServices.CollectionChanges.RunAsync(ReloadRequestsAsync));

    Coalescer HistoryReload => field ??= new(() => history.RefreshAsync(CancellationToken.None));

    Coalescer EnvironmentsReload => field ??= new(() => environments.LoadAsync(CancellationToken.None));

    Coalescer WorkflowsReload => field ??= new(ReloadWorkflowsAsync);

    public RequestTreeViewModel Tree => tree;

    public HistoryViewModel History => history;

    public EnvironmentsViewModel Environments => environments;

    public CredentialsViewModel Credentials => credentials;

    public WorkflowsViewModel Workflows => workflows;

    public ClipboardViewModel Clipboard => clipboard;

    public ObservableCollection<RequestTabViewModel> Tabs { get; } = tree.Follow([]);

    public TabSession Session => new([.. Tabs.Where(tab => tab.IsSaved && library.Exists(tab.Id)).Select(tab => $"{tab.Id}")], SelectedTab is { IsSaved: true } selected ? $"{selected.Id}" : null);

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
        await credentials.LoadAsync(CancellationToken.None);
        await WorkflowsChangedAsync();
        try
        {
            var saved = await settingsStore.LoadAsync(CancellationToken.None);
            tree.ShowWholeFolders = saved.SearchWholeFolders;
            if (saved.Session is { } session)
            {
                foreach (var key in session.Requests.Distinct())
                {
                    if (Guid.TryParse(key, out var id) && RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(node => !node.IsFolder && !node.IsDraft && node.Id == id) is { } node)
                    {
                        await OpenAsync(node);
                    }
                }
                SelectedTab = Tabs.FirstOrDefault(tab => tab.IsSaved && $"{tab.Id}" == session.Selected) ?? SelectedTab;
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

    public async Task LanguageChangedAsync()
    {
        history.Relabel();
        RelabelTabs();
        Workflow?.Relabel();
        // A request or workflow that cannot be read is shown with a text in the language.
        tree.Refresh();
        await WorkflowsChangedAsync();
        await HistoryChangedAsync();
    }

    // Each environment has its own OAuth tokens, so the tabs show the chosen environment's, and the workflow shows which names it has.
    // The responses stay as they are shown, as the environment does not change them.
    public void EnvironmentChosen()
    {
        foreach (var tab in Tabs)
        {
            tab.RelabelRequest();
        }
        Workflow?.EnvironmentChosen();
        credentials.EnvironmentChosen();
    }

    public void NewTab()
    {
        logger.LogDebug("Opened a new tab");
        Add(new(tabServices, ApiRequest.New()) { Number = ++_lastNumber });
    }

    public async Task OpenAsync(RequestNodeViewModel node)
    {
        if ((node.IsDraft ? node.Tab : TabOf(node.Id)) is { } open)
        {
            open.RequestSection = RequestSection.Body;
            Show(open);
            return;
        }
        if (!_opening.Add(node.Id))
        {
            return;
        }
        try
        {
            if (await library.LoadAsync(node.Id, CancellationToken.None) is not { } request)
            {
                await tree.LoadAsync(CancellationToken.None);
                return;
            }
            var tab = new RequestTabViewModel(tabServices, request, saved: true);
            Add(tab);
            await tab.LoadSecretsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not open {Name}", node.Name);
            dialogs.Tell(translator.Of("Open.Failed"), translator.DetailsOf(exception));
        }
        finally
        {
            _opening.Remove(node.Id);
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
        // The request it ran may since have been renamed or moved, so it is suggested as it is now.
        var request = tree.Collection.RequestOf(entry.Request.Id) is { } saved ? entry.Request with { Name = saved.Name, FolderId = saved.FolderId } : entry.Request;
        var tab = new RequestTabViewModel(tabServices, request, historyName: item.File.Name) { Number = request.Name.Length == 0 ? ++_lastNumber : 0 };
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
        if (dialogs.AskName(translator.Of("Folder.Title"), "", translator.Of("Folder.Create"), tabServices.ProblemOfName) is not { } name)
        {
            return;
        }
        await CreateFolderAsync(null, name);
    }

    public async Task NewDraftAsync(RequestNodeViewModel folder)
    {
        var tab = new RequestTabViewModel(tabServices, ApiRequest.New() with { FolderId = folder.Id }, draft: true) { Number = ++_lastNumber };
        Add(tab);
        await tab.UpdateAuthSourceAsync();
    }

    public async Task NewSubfolderAsync(RequestNodeViewModel parent)
    {
        if (dialogs.AskName(translator.Format("Folder.TitleIn", tree.Collection.FolderPathOf(parent.Id)), "", translator.Of("Folder.Create"), tabServices.ProblemOfName) is not { } name)
        {
            return;
        }
        if (await CreateFolderAsync(parent.Id, name))
        {
            tree.ExpandTo(parent.Id);
        }
    }

    async Task<bool> CreateFolderAsync(Guid? parent, string name)
    {
        try
        {
            await library.CreateFolderAsync(new() { Id = Guid.NewGuid(), Name = name, ParentId = parent }, CancellationToken.None);
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
        if (dialogs.AskName(translator.Of("Rename.Title"), node.Name, translator.Of("Common.Save"), tabServices.ProblemOfName) is not { } name || name == node.Name)
        {
            return;
        }
        await ChangeCollectionAsync(() => RenameToAsync(node.Id, name));
    }

    public async Task RenameTabAsync(RequestTabViewModel tab)
    {
        if (dialogs.AskName(translator.Of("Rename.Title"), tab.Title, translator.Of("Common.Save"), tabServices.ProblemOfName) is not { } name)
        {
            return;
        }
        await ChangeCollectionAsync(async () =>
        {
            if (!Tabs.Contains(tab))
            {
                return;
            }
            if (!tab.IsSaved)
            {
                await tab.SaveCoreAsync(name);
                return;
            }
            if (name != tab.Name)
            {
                await RenameToAsync(tab.Id, name);
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
            var open = node.Tab ?? TabOf(node.Id);
            var source = open?.ToRequest() ?? await library.LoadAsync(node.Id, CancellationToken.None);
            if (source is null)
            {
                return;
            }
            var folder = node.IsDraft ? open!.FolderId : node.ParentId;
            // A row that cannot be read shows a text of its own, so the copy takes the name of what is copied.
            var stem = node.IsDraft ? open!.Title : source.Name;
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
                name = $"{stem} ({number++})";
            } while (tree.IsTaken(folder, name));
            var clone = source with { Id = Guid.NewGuid(), Name = name, FolderId = folder };
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
                await library.CreateAsync(clone, CancellationToken.None);
            }
            catch
            {
                await tabServices.Secrets.DeleteAsync(clone.Id, CancellationToken.None);
                throw;
            }
            await tree.LoadAsync(CancellationToken.None);
            await tree.PlaceAsync($"{clone.Id}", folder, node.OrderKey, DropPosition.After);
            await OpenAsync(new RequestNodeViewModel(clone.Id, clone.Name, clone.Method, false) { ParentId = folder });
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not clone {Name}", node.Name);
            dialogs.Tell(translator.Of("Clone.Failed"), translator.DetailsOf(exception));
        }
    }

    // A request dropped on another request goes into the folder that one is in, and one dropped beside the folders goes to the top.
    public bool CanMove(RequestNodeViewModel node, RequestNodeViewModel? target, DropPosition position)
    {
        var folder = DestinationOf(target, position);
        // While searching, rows are hidden, so where a drop would land cannot be seen.
        return !tree.IsSearching && node != target && (target is null || node.OrderKey != target.OrderKey)
            && (!node.IsFolder || !tree.Collection.FoldersDownTo(folder).Any(above => above.Id == node.Id))
            && (folder is null || tree.Collection.FolderOf(folder) is not null);
    }

    static Guid? DestinationOf(RequestNodeViewModel? target, DropPosition position) => target is null ? null : position == DropPosition.Inside && target.IsFolder ? target.Id : target.ParentId;

    public Task MoveAsync(RequestNodeViewModel node, RequestNodeViewModel? target, DropPosition position = DropPosition.Inside) => ChangeCollectionAsync(() => MoveCoreAsync(node, target, position));

    async Task MoveCoreAsync(RequestNodeViewModel node, RequestNodeViewModel? target, DropPosition position)
    {
        var current = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(row => row.OrderKey == node.OrderKey);
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
        try
        {
            if (node.IsDraft)
            {
                node.Tab!.MoveTo(folder);
                tree.Refresh();
            }
            else if (node.ParentId != folder && !await MoveToAsync(node, folder))
            {
                return;
            }
            await tree.PlaceAsync(node.OrderKey, folder, position == DropPosition.Inside ? null : target?.OrderKey, position);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not move {Name}", node.Name);
            dialogs.Tell(translator.Of("Move.Failed"), translator.DetailsOf(exception));
        }
    }

    // A folder is moved with everything in it by changing its own file only.
    async Task<bool> MoveToAsync(RequestNodeViewModel node, Guid? folder)
    {
        try
        {
            if (node.IsFolder)
            {
                await tabServices.AuthRefresh.SaveAsync(() => library.MoveFolderAsync(node.Id, folder, CancellationToken.None), CancellationToken.None);
            }
            else
            {
                await library.MoveAsync(node.Id, folder, CancellationToken.None);
                TabOf(node.Id)?.MoveTo(folder);
            }
            await tree.LoadAsync(CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not move {Name}", node.Name);
            dialogs.Tell(translator.Of("Move.Failed"), translator.DetailsOf(exception));
            await tree.LoadAsync(CancellationToken.None);
            return false;
        }
    }

    async Task RenameToAsync(Guid id, string name)
    {
        try
        {
            await library.RenameAsync(id, name, CancellationToken.None);
            TabOf(id)?.Rename(name);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename {Id} to {NewName}", id, name);
            dialogs.Tell(translator.Of("Rename.Failed"), translator.DetailsOf(exception));
        }
        await tree.LoadAsync(CancellationToken.None);
    }

    public async Task RenameFolderAsync(RequestNodeViewModel folder)
    {
        if (dialogs.AskName(translator.Of("RenameFolder.Title"), folder.Name, translator.Of("Common.Save"), tabServices.ProblemOfName) is not { } name
            || name == folder.Name)
        {
            return;
        }
        await ChangeCollectionAsync(async () =>
        {
            try
            {
                await tabServices.AuthRefresh.SaveAsync(() => library.RenameFolderAsync(folder.Id, name, CancellationToken.None), CancellationToken.None);
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogError(exception, "Could not rename the folder {Name} to {NewName}", folder.Name, name);
                dialogs.Tell(translator.Of("RenameFolder.Failed"), translator.DetailsOf(exception));
            }
            await tree.LoadAsync(CancellationToken.None);
        });
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
            logger.LogError(exception, "Could not delete {Name}", node.Name);
            dialogs.Tell(translator.Of("Delete.Failed"), translator.DetailsOf(exception));
        }
    }

    public Task DeleteFolderAsync(RequestNodeViewModel folder) => ChangeCollectionAsync(() => DeleteFolderCoreAsync(folder));

    async Task DeleteFolderCoreAsync(RequestNodeViewModel folder)
    {
        folder = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(node => node.IsFolder && node.Id == folder.Id) ?? folder;
        var inside = RequestTreeViewModel.Flatten([folder]).ToList();
        var requests = inside.Where(node => !node.IsFolder && !node.IsDraft).Select(node => node.Id).ToHashSet();
        var drafts = inside.Where(node => node.IsDraft).Select(node => node.Tab!).ToList();
        var unsaved = Tabs.Where(tab => drafts.Contains(tab) || tab.IsDirty && tab.IsSaved && requests.Contains(tab.Id)).Select(tab => tab.Title).ToList();
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
            logger.LogError(exception, "Could not delete the folder {Name}", folder.Name);
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
        node = RequestTreeViewModel.Flatten(tree.Nodes).FirstOrDefault(row => row.IsFolder == node.IsFolder && row.Id == node.Id) ?? node;
        var inside = RequestTreeViewModel.Flatten([node]).ToList();
        var requests = inside.Where(row => !row.IsFolder && !row.IsDraft).Select(row => row.Id);
        var drafts = inside.Where(row => row.IsDraft).Select(row => row.Tab!).ToList();
        try
        {
            await deletion.DeleteAsync(node.Id, node.IsFolder, CancellationToken.None);
        }
        finally
        {
            var removed = requests.Where(id => !library.Exists(id)).ToList();
            foreach (var id in removed)
            {
                if (TabOf(id) is { } tab)
                {
                    CloseTab(tab);
                }
            }
            foreach (var tab in Tabs.Where(tab => !tab.OwnsId && removed.Contains(tab.Id)))
            {
                tab.Auth.ForgetSavedSecrets();
            }
            foreach (var draft in drafts.Where(tab => tab.FolderId is { } folder && !library.FolderExists(folder)))
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

    public async Task OpenWorkflowAsync(Guid id)
    {
        if (Workflow is { } open && open.Id == id)
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
        var workflow = new WorkflowViewModel(workflowServices, id);
        if (!await workflow.LoadAsync())
        {
            await WorkflowsChangedAsync();
            return;
        }
        logger.LogInformation("Opened the workflow {Name}", workflow.Name);
        var closing = Workflow?.CloseAsync() ?? Task.CompletedTask;
        Workflow = workflow;
        Section = SidebarSection.Workflows;
        await closing;
    }

    public async Task NewWorkflowAsync()
    {
        if (dialogs.AskName(translator.Of("Sidebar.NewWorkflow"), "", translator.Of("Folder.Create"), tabServices.ProblemOfName) is not { } name)
        {
            return;
        }
        Workflow created;
        try
        {
            created = await workflowServices.Library.CreateAsync(name, CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not create the workflow {Name}", name);
            dialogs.Tell(translator.Of("Workflow.CreateFailed"), translator.DetailsOf(exception));
            return;
        }
        await WorkflowsChangedAsync();
        await OpenWorkflowAsync(created.Id);
    }

    public async Task RenameWorkflowAsync(WorkflowItem item)
    {
        if (dialogs.AskName(translator.Of("Workflow.RenameTitle"), item.Name, translator.Of("Common.Save"), tabServices.ProblemOfName) is not { } newName
            || newName == item.Name)
        {
            return;
        }
        try
        {
            await workflowServices.Library.RenameAsync(item.Id, newName, CancellationToken.None);
            if (Workflow is { } open && open.Id == item.Id)
            {
                open.Rename(newName);
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename the workflow {Name} to {NewName}", item.Name, newName);
            dialogs.Tell(translator.Of("Workflow.RenameFailed"), translator.DetailsOf(exception));
        }
        await WorkflowsChangedAsync();
    }

    public async Task DeleteWorkflowAsync(WorkflowItem item)
    {
        if (!dialogs.Confirm(translator.Of("Workflow.DeleteTitle"), translator.Format("Delete.Message", item.Name), translator.Of("Delete.Confirm"), []))
        {
            return;
        }
        try
        {
            // The steps' secrets go with the workflow, but only when it can be read, so they are known.
            var owners = await StepIdsOfAsync(item.Id);
            await workflowServices.Library.DeleteAsync(item.Id, CancellationToken.None);
            if (Workflow is { } open && open.Id == item.Id)
            {
                await CloseWorkflowAsync();
            }
            await workflowServices.ForgetSecretsAsync(owners);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete the workflow {Name}", item.Name);
            dialogs.Tell(translator.Of("Workflow.DeleteFailed"), translator.DetailsOf(exception));
        }
        await WorkflowsChangedAsync();
    }

    async Task<IReadOnlyList<Guid>> StepIdsOfAsync(Guid id)
    {
        try
        {
            return await workflowServices.Library.LoadAsync(id, CancellationToken.None) is { } workflow ? [.. WorkflowLibrary.SecretOwnersOf(workflow)] : [];
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the workflow {Id}, so its secrets are kept", id);
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
        await folderAuth.LoadAsync(folder.Id, string.Join(" / ", tree.Collection.FoldersDownTo(folder.Id).Select(above => above.Name)), CancellationToken.None);
        dialogs.EditFolderAuth(folderAuth);
    }

    async Task ReloadRequestsAsync()
    {
        await tree.LoadAsync(CancellationToken.None);
        foreach (var tab in Tabs.ToList())
        {
            try
            {
                tab.RefreshFolder();
                // A tab whose file was gone finds it again when it comes back.
                if (!(tab.IsSaved || tab.OwnsId && !tab.IsDraft) || !await FollowAsync(tab))
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

    // The file wins over the tab, also for its name and its folder.
    async Task<bool> FollowAsync(RequestTabViewModel tab)
    {
        // A request that another tab has open is left to that tab, so two tabs never save over each other.
        if (!tab.IsSaved && TabOf(tab.Id) is not null)
        {
            return false;
        }
        if (await library.LoadAsync(tab.Id, CancellationToken.None) is not { } request)
        {
            if (tab.IsSaved)
            {
                logger.LogInformation("{Name} was removed on disk", tab.Name);
                tab.Unlink();
            }
            return false;
        }
        if (request.Name != tab.Name)
        {
            tab.Rename(request.Name);
        }
        if (request.FolderId != tab.FolderId)
        {
            tab.MoveTo(request.FolderId);
        }
        if (tab.ReloadIfChanged(request))
        {
            logger.LogInformation("{Name} changed on disk and was reloaded", request.Name);
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

    RequestTabViewModel? TabOf(Guid id) => Tabs.FirstOrDefault(tab => tab.IsSaved && tab.Id == id);
}
