using System.Collections.ObjectModel;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class MainViewModel(RequestTreeViewModel tree, HistoryViewModel history, EnvironmentsViewModel environments, RequestTabServices services, ILogger<MainViewModel> logger) : ObservableObject
{
    RequestLibrary Library => services.Library;

    IDialogs Dialogs => services.Dialogs;

    Translator Translator => services.Translator;

    public RequestTreeViewModel Tree => tree;

    public HistoryViewModel History => history;

    public EnvironmentsViewModel Environments => environments;

    public ObservableCollection<RequestTabViewModel> Tabs { get; } = [];

    public RequestTabViewModel? SelectedTab { get; set => Set(ref field, value); }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await tree.LoadAsync(cancellationToken);
        await history.LoadAsync(cancellationToken);
        await environments.LoadAsync(cancellationToken);
        if (Tabs.Count == 0)
        {
            NewTab();
        }
    }

    public void NewTab() => Add(new(services, ApiRequest.New()));

    public async Task OpenAsync(RequestNodeViewModel node)
    {
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }
        if (Tabs.FirstOrDefault(tab => tab.Name == node.Path) is { } open)
        {
            SelectedTab = open;
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
        tab.Sent -= Tab_Sent;
        tab.Saved -= Tab_Saved;
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }
        SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
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
        if (Dialogs.AskName(Translator.Of("Rename.Title"), node.Path, Translator.Of("Editor.Save"), name => name == node.Path ? null : services.ProblemOfName(name)) is not { } name || name == node.Path)
        {
            return;
        }
        try
        {
            await Library.RenameAsync(node.Path, name, CancellationToken.None);
            Tabs.FirstOrDefault(tab => tab.Name == node.Path)?.Rename(name);
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
            Tabs.FirstOrDefault(tab => tab.Name == node.Path)?.Unlink();
            await tree.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not delete {Name}", node.Path);
            Dialogs.Tell(Translator.Of("Delete.Failed"), exception.Message);
        }
    }

    void Add(RequestTabViewModel tab)
    {
        tab.Sent += Tab_Sent;
        tab.Saved += Tab_Saved;
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    void Tab_Sent() => _ = history.LoadAsync(CancellationToken.None);

    void Tab_Saved() => _ = tree.LoadAsync(CancellationToken.None);

    string TitleOf(RequestTabViewModel tab) => tab.Title ?? Translator.Of("Tab.New");

    string? ProblemOfFolder(string name) => RequestLibrary.IsValidName(name) ? null : Translator.Of("Save.Invalid");
}
