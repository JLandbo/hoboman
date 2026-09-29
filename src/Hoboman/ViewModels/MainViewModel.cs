using System.Collections.ObjectModel;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class MainViewModel(RequestTreeViewModel tree, HistoryViewModel history, EnvironmentsViewModel environments, RequestTabServices services, ILogger<MainViewModel> logger) : ObservableObject
{
    readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase);

    Coalescer RequestsReload => field ??= new(ReloadRequestsAsync);

    Coalescer HistoryReload => field ??= new(() => history.RefreshAsync(CancellationToken.None));

    Coalescer EnvironmentsReload => field ??= new(() => environments.LoadAsync(CancellationToken.None));

    RequestLibrary Library => services.Library;

    IDialogs Dialogs => services.Dialogs;

    Translator Translator => services.Translator;

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
        return HistoryChangedAsync();
    }

    public void NewTab() => Add(new(services, ApiRequest.New()));

    public async Task OpenAsync(RequestNodeViewModel node)
    {
        if (Tabs.FirstOrDefault(tab => SameName(tab.Name, node.Path)) is { } open)
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
            if (await Library.LoadAsync(node.Path, CancellationToken.None) is not { } request)
            {
                await tree.LoadAsync(CancellationToken.None);
                return;
            }
            var tab = new RequestTabViewModel(services, request, name: node.Path);
            Add(tab);
            await tab.LoadSecretsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not open {Name}", node.Path);
            Dialogs.Tell(Translator.Of("Open.Failed"), exception.Message);
        }
        finally
        {
            _opening.Remove(node.Path);
        }
    }

    public async Task OpenAsync(HistoryItemViewModel item)
    {
        var tab = new RequestTabViewModel(services, item.Entry.Request, suggestedName: item.Entry.Name, fromHistory: true);
        tab.Show(item.Entry);
        Add(tab);
        await tab.LoadSecretsAsync(CancellationToken.None);
    }

    public void Close(RequestTabViewModel tab)
    {
        if (tab.IsDirty && !Dialogs.Confirm(Translator.Of("Close.TabTitle"), Translator.Format("Close.TabMessage", TitleOf(tab)), Translator.Of("Close.Confirm"), []))
        {
            return;
        }
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
        var unsaved = Tabs.Where(tab => tab.IsDirty).Select(TitleOf).ToList();
        return unsaved.Count == 0 || Dialogs.Confirm(Translator.Of("Close.Title"), Translator.Of("Close.Message"), Translator.Of("Close.Confirm"), unsaved);
    }

    public async Task NewFolderAsync()
    {
        if (Dialogs.AskName(Translator.Of("Folder.Title"), "", Translator.Of("Folder.Create"), ProblemOfFolder) is not { } name)
        {
            return;
        }
        try
        {
            await Library.CreateFolderAsync(name, CancellationToken.None);
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not create the folder {Name}", name);
            Dialogs.Tell(Translator.Of("Folder.Failed"), exception.Message);
        }
    }

    public async Task RenameAsync(RequestNodeViewModel node)
    {
        if (Dialogs.AskName(Translator.Of("Rename.Title"), node.Path, Translator.Of("Editor.Save"), candidate => SameName(candidate, node.Path) ? null : services.ProblemOfName(candidate)) is not { } name || name == node.Path)
        {
            return;
        }
        try
        {
            await Library.RenameAsync(node.Path, name, CancellationToken.None);
            Tabs.FirstOrDefault(tab => SameName(tab.Name, node.Path))?.Rename(name);
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not rename {Name}", node.Path);
            Dialogs.Tell(Translator.Of("Rename.Failed"), exception.Message);
        }
    }

    public async Task DeleteAsync(RequestNodeViewModel node)
    {
        if (!Dialogs.Confirm(Translator.Of("Delete.Title"), Translator.Format("Delete.Message", node.Name), Translator.Of("Delete.Confirm"), []))
        {
            return;
        }
        try
        {
            await Library.DeleteAsync(node.Path, CancellationToken.None);
            Tabs.FirstOrDefault(tab => SameName(tab.Name, node.Path))?.Unlink();
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete {Name}", node.Path);
            Dialogs.Tell(Translator.Of("Delete.Failed"), exception.Message);
        }
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
                tab.ShowProblem(new(Translator.Of("Open.Failed"), exception.Message));
            }
        }
    }

    // The file wins over the tab, and a tab whose file was moved or renamed finds it again by its id.
    async Task FollowAsync(RequestTabViewModel tab)
    {
        var name = tab.Name;
        var request = name is null ? null : await Library.LoadAsync(name, CancellationToken.None);
        if (request is null && tree.NameOf(tab.Id) is { } moved)
        {
            name = moved;
            request = await Library.LoadAsync(moved, CancellationToken.None);
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

    string TitleOf(RequestTabViewModel tab) => tab.Title ?? Translator.Of("Tab.New");

    string? ProblemOfFolder(string name) => RequestLibrary.IsValidName(name) ? null : Translator.Of("Save.Invalid");

    // Windows does not tell upper and lower case apart in file names.
    static bool SameName(string? name, string? other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
}
