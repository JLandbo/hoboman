using System.Collections.ObjectModel;
using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class MainViewModel(
    RequestTreeViewModel tree,
    HistoryViewModel history,
    EnvironmentsViewModel environments,
    SettingsViewModel settings,
    EnvironmentEditorViewModel environmentEditor,
    FolderAuthViewModel folderAuth,
    RequestTabServices tabServices,
    RequestLibrary library,
    SecretStore secrets,
    IDialogs dialogs,
    ClipboardViewModel clipboard,
    Translator translator,
    ILogger<MainViewModel> logger) : ObservableObject
{
    readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase);
    int _lastNumber;

    Coalescer RequestsReload => field ??= new(ReloadRequestsAsync);

    Coalescer HistoryReload => field ??= new(() => history.RefreshAsync(CancellationToken.None));

    Coalescer EnvironmentsReload => field ??= new(() => environments.LoadAsync(CancellationToken.None));

    public RequestTreeViewModel Tree => tree;

    public HistoryViewModel History => history;

    public EnvironmentsViewModel Environments => environments;

    public ClipboardViewModel Clipboard => clipboard;

    public ObservableCollection<RequestTabViewModel> Tabs { get; } = tree.Follow([]);

    public RequestTabViewModel? SelectedTab
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                tree.Activate(value);
            }
        }
    }

    public async Task LoadAsync()
    {
        await RequestsChangedAsync();
        await HistoryChangedAsync();
        await EnvironmentsChangedAsync();
        if (Tabs.Count == 0)
        {
            NewTab();
        }
    }

    public Task RequestsChangedAsync() => RequestsReload.RunAsync();

    public Task HistoryChangedAsync() => HistoryReload.RunAsync();

    public Task EnvironmentsChangedAsync() => EnvironmentsReload.RunAsync();

    public Task LanguageChangedAsync()
    {
        history.Relabel();
        RelabelTabs();
        return HistoryChangedAsync();
    }

    // Each environment has its own OAuth tokens, so the tabs show the chosen environment's.
    public void EnvironmentChosen() => RelabelTabs();

    public void NewTab()
    {
        logger.LogDebug("Opened a new tab");
        Add(new(tabServices, ApiRequest.New()) { Number = ++_lastNumber });
    }

    public async Task OpenAsync(RequestNodeViewModel node)
    {
        if ((node.IsDraft ? node.Tab : TabOf(node.Path)) is { } open)
        {
            SelectedTab = open;
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
            SelectedTab = open;
            return;
        }
        logger.LogInformation("Opened the call to {Address} from the history", item.Address);
        var entry = item.File.Entry;
        var tab = new RequestTabViewModel(tabServices, entry.Request, suggestedName: entry.Name, historyName: item.File.Name) { Number = entry.Name is null ? ++_lastNumber : 0 };
        if (Tabs.FirstOrDefault(open => open.IsPreview) is { } preview)
        {
            preview.Close();
            Tabs[Tabs.IndexOf(preview)] = tab;
        }
        else
        {
            Tabs.Add(tab);
        }
        SelectedTab = tab;
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

    public bool CanClose()
    {
        var unsaved = Tabs.Where(tab => tab.IsDirty).Select(tab => tab.Title).ToList();
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
        if (dialogs.AskName(translator.Of("Folder.Title"), "", translator.Of("Folder.Create"), ProblemOfFolder) is not { } name)
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
        if (dialogs.AskName(translator.Of("Rename.Title"), node.Path, translator.Of("Common.Save"), candidate => SameName(candidate, node.Path) ? null : tabServices.ProblemOfName(candidate)) is not { } name || name == node.Path)
        {
            return;
        }
        await RenameToAsync(node, name, translator.Of("Rename.Failed"));
    }

    // A request dropped on another request goes into the folder that one is in, and one dropped beside the folders goes to the top.
    public async Task MoveAsync(RequestNodeViewModel node, RequestNodeViewModel? target)
    {
        var folder = target is null ? null : target.IsFolder ? target.Path : RequestLibrary.ParentOf(target.Path);
        var name = folder is null ? node.Name : $"{folder}/{node.Name}";
        if (SameName(name, node.Path))
        {
            return;
        }
        // Otherwise the request would seem to vanish into a closed folder.
        if (target is { IsFolder: true })
        {
            target.IsExpanded = true;
        }
        if (tabServices.ProblemOfName(name) is { } problem)
        {
            dialogs.Tell(translator.Of("Move.Failed"), problem);
            return;
        }
        await RenameToAsync(node, name, translator.Of("Move.Failed"));
    }

    async Task RenameToAsync(RequestNodeViewModel node, string name, string failed)
    {
        try
        {
            await library.RenameAsync(node.Path, name, CancellationToken.None);
            TabOf(node.Path)?.Rename(name);
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename {Name} to {NewName}", node.Path, name);
            dialogs.Tell(failed, translator.DetailsOf(exception));
        }
    }

    public async Task RenameFolderAsync(RequestNodeViewModel folder)
    {
        if (dialogs.AskName(translator.Of("RenameFolder.Title"), folder.Path, translator.Of("Common.Save"), candidate => SameName(candidate, folder.Path) ? null : ProblemOfRenamedFolder(candidate, folder.Path)) is not { } name
            || name == folder.Path)
        {
            return;
        }
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
            await tree.LoadAsync(CancellationToken.None);
            foreach (var node in RequestTreeViewModel.Flatten(tree.Nodes).Where(node => opened.Contains(node.Path)))
            {
                node.IsExpanded = true;
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename the folder {Name} to {NewName}", folder.Path, name);
            dialogs.Tell(translator.Of("RenameFolder.Failed"), translator.DetailsOf(exception));
        }

        string Moved(string path) => $"{name}{path[folder.Path.Length..]}";
    }

    public async Task DeleteAsync(RequestNodeViewModel node)
    {
        if (!dialogs.Confirm(translator.Of("Delete.Title"), translator.Format("Delete.Message", node.Name), translator.Of("Delete.Confirm"), []))
        {
            return;
        }
        try
        {
            var id = tree.IdOf(node.Path);
            var tab = TabOf(node.Path);
            await library.DeleteAsync(node.Path, CancellationToken.None);
            tab?.Unlink();
            await tree.LoadAsync(CancellationToken.None);
            await DeleteSecretsIfUnusedAsync(id);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete {Name}", node.Path);
            dialogs.Tell(translator.Of("Delete.Failed"), translator.DetailsOf(exception));
        }
    }

    public async Task DeleteFolderAsync(RequestNodeViewModel folder)
    {
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
            var ids = requests.Select(tree.IdOf).ToList();
            var folders = new List<(string Path, Guid Id)>();
            foreach (var node in inside.Where(node => node.IsFolder))
            {
                folders.Add((node.Path, await FolderIdOfAsync(node.Path)));
            }
            try
            {
                await tabServices.AuthRefresh.SaveAsync(() => library.DeleteFolderAsync(folder.Path, CancellationToken.None), CancellationToken.None);
            }
            // A file that cannot be deleted stops only itself, so what is gone is let go of either way, and what is left is kept.
            finally
            {
                foreach (var name in requests.Where(name => !library.Exists(name)))
                {
                    TabOf(name)?.Unlink();
                }
                foreach (var draft in drafts.Where(tab => tab.Destination is { } destination && !library.FolderExists(destination)))
                {
                    draft.Unlink();
                    await draft.UpdateAuthSourceAsync();
                }
                await tree.LoadAsync(CancellationToken.None);
                foreach (var id in ids)
                {
                    await DeleteSecretsIfUnusedAsync(id);
                }
                foreach (var (path, id) in folders)
                {
                    if (id != Guid.Empty && !library.FolderExists(path) && !await library.SharesFolderIdAsync(path, id, CancellationToken.None))
                    {
                        await secrets.DeleteAsync(id, CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete the folder {Name}", folder.Path);
            dialogs.Tell(translator.Of("DeleteFolder.Failed"), translator.DetailsOf(exception));
        }
    }

    // A copy with the same id shares the secrets, so they stay while any request still uses them.
    async Task DeleteSecretsIfUnusedAsync(Guid id)
    {
        if (id == Guid.Empty || tree.IsUsed(id))
        {
            return;
        }
        await secrets.DeleteAsync(id, CancellationToken.None);
        foreach (var open in Tabs.Where(open => open.Id == id))
        {
            open.Auth.ForgetSavedSecrets();
        }
    }

    // A folder whose settings cannot be read is still deleted; only its secrets cannot be found.
    async Task<Guid> FolderIdOfAsync(string name)
    {
        try
        {
            return (await library.LoadFolderAsync(name, CancellationToken.None))?.Id ?? Guid.Empty;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the settings of the folder {Name}", name);
            return Guid.Empty;
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
        SelectedTab = tab;
    }

    RequestTabViewModel? TabOf(string name) => Tabs.FirstOrDefault(tab => SameName(tab.Name, name));

    string? ProblemOfFolder(string name) => RequestLibrary.IsValidName(name) ? null : translator.Of("Save.Invalid");

    string? ProblemOfNewFolder(string name) => ProblemOfFolder(name) ?? (library.FolderExists(name) ? translator.Of("Folder.Exists") : null);

    // A folder cannot go inside itself.
    string? ProblemOfRenamedFolder(string name, string path) =>
        ProblemOfFolder(name)
        ?? (name.StartsWith($"{path}/", StringComparison.OrdinalIgnoreCase) ? translator.Of("Save.Invalid")
        : library.FolderExists(name) ? translator.Of("Folder.Exists")
        : null);

    // Windows does not tell upper and lower case apart in file names.
    static bool SameName(string? name, string? other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
}
