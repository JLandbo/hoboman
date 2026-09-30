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

    public ObservableCollection<RequestTabViewModel> Tabs { get; } = [];

    public RequestTabViewModel? SelectedTab { get; set => Set(ref field, value); }

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
        foreach (var tab in Tabs)
        {
            tab.Relabel();
        }
        return HistoryChangedAsync();
    }

    public void NewTab()
    {
        logger.LogDebug("Opened a new tab");
        Add(new(tabServices, ApiRequest.New()) { Number = ++_lastNumber });
    }

    public async Task OpenAsync(RequestNodeViewModel node)
    {
        if (TabOf(node.Path) is { } open)
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
        tab.Cancel();
        var index = Tabs.IndexOf(tab);
        var wasSelected = SelectedTab == tab;
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
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
        try
        {
            await library.CreateFolderAsync(name, CancellationToken.None);
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not create the folder {Name}", name);
            dialogs.Tell(translator.Of("Folder.Failed"), translator.DetailsOf(exception));
        }
    }

    public async Task RenameAsync(RequestNodeViewModel node)
    {
        if (dialogs.AskName(translator.Of("Rename.Title"), node.Path, translator.Of("Common.Save"), candidate => SameName(candidate, node.Path) ? null : tabServices.ProblemOfName(candidate)) is not { } name || name == node.Path)
        {
            return;
        }
        try
        {
            await library.RenameAsync(node.Path, name, CancellationToken.None);
            TabOf(node.Path)?.Rename(name);
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename {Name}", node.Path);
            dialogs.Tell(translator.Of("Rename.Failed"), translator.DetailsOf(exception));
        }
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
            // A copy with the same id shares the secrets, so they stay while any request still uses them.
            if (id != Guid.Empty && !tree.IsUsed(id))
            {
                await secrets.DeleteAsync(id, CancellationToken.None);
                foreach (var open in Tabs.Where(open => open.Id == id))
                {
                    open.Auth.ForgetSavedSecrets();
                }
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete {Name}", node.Path);
            dialogs.Tell(translator.Of("Delete.Failed"), translator.DetailsOf(exception));
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
        foreach (var tab in Tabs.Where(tab => tab.Name is not null || tab.OwnsId).ToList())
        {
            try
            {
                await FollowAsync(tab);
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Could not reload {Name}", tab.Name);
                tab.ShowFileProblem(translator.DetailsOf(exception));
            }
        }
    }

    // The file wins over the tab, and a tab whose file was moved or renamed finds it again by its id.
    async Task FollowAsync(RequestTabViewModel tab)
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
            return;
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
        }
    }

    void Add(RequestTabViewModel tab)
    {
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    RequestTabViewModel? TabOf(string name) => Tabs.FirstOrDefault(tab => SameName(tab.Name, name));

    string? ProblemOfFolder(string name) => RequestLibrary.IsValidName(name) ? null : translator.Of("Save.Invalid");

    // Windows does not tell upper and lower case apart in file names.
    static bool SameName(string? name, string? other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
}
