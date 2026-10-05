using System.Collections.ObjectModel;
using System.ComponentModel;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class RequestTreeViewModel(RequestLibrary library, RequestSnapshot snapshot, IDialogs dialogs, Translator translator, ILogger<RequestTreeViewModel> logger) : ObservableObject
{
    IReadOnlyList<string> _order = [];
    // The folders open when a search began, so clearing it opens the same ones again.
    HashSet<Guid>? _expandedBeforeSearch;
    ObservableCollection<RequestTabViewModel> _tabs = [];
    readonly HashSet<RequestTabViewModel> _followed = [];
    RequestTabViewModel? _selected;
    int _loadVersion;
    bool _orderSaveFailed;

    public ObservableCollection<RequestNodeViewModel> Nodes { get; } = [];

    public RequestCollection Collection => snapshot.Current;

    public string Search
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Filter();
            }
        }
    } = "";

    // A folder whose name matches shows all it holds, so searching for "docs" shows the docs folder.
    public bool ShowWholeFolders
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Filter();
            }
        }
    } = true;

    public bool IsSearching => Search.Trim().Length > 0;

    public bool NothingFound => IsSearching && Nodes.Count > 0 && !Nodes.Any(node => node.IsShown);

    public event Action<RequestNodeViewModel>? Revealed;

    internal void ResetOrderFailure() => _orderSaveFailed = false;

    public Task SaveOrderAsync()
    {
        _loadVersion++;
        _order = [.. Flatten(Nodes).Select(node => node.OrderKey)];
        return PersistOrderAsync();
    }

    async Task PersistOrderAsync()
    {
        try
        {
            await library.SaveOrderAsync([.. _order.Where(key => !key.StartsWith('\0'))], CancellationToken.None);
            _orderSaveFailed = false;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not save the request order");
            if (_orderSaveFailed)
            {
                return;
            }
            _orderSaveFailed = true;
            dialogs.Tell(translator.Of("Order.SaveFailed"), translator.DetailsOf(exception));
        }
    }

    public async Task PlaceAsync(string key, Guid? parent, string? relativeTo, DropPosition position)
    {
        var siblings = FolderRowOf(parent)?.Children ?? Nodes;
        if (siblings.FirstOrDefault(node => node.OrderKey == key) is not { } moved)
        {
            return;
        }
        var ordered = siblings.Where(node => node != moved).ToList();
        var target = ordered.FindIndex(node => node.OrderKey == relativeTo);
        var index = target < 0 ? ordered.Count : target + (position == DropPosition.After ? 1 : 0);
        siblings.Move(siblings.IndexOf(moved), index);
        await SaveOrderAsync();
        Reveal(moved);
    }

    public void Refresh() => Show();

    public ObservableCollection<RequestTabViewModel> Follow(ObservableCollection<RequestTabViewModel> tabs)
    {
        _tabs.CollectionChanged -= TabsChanged;
        _tabs = tabs;
        _tabs.CollectionChanged += TabsChanged;
        TabsChanged(null, null);
        return tabs;
    }

    void TabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? args)
    {
        foreach (var tab in _followed.Where(tab => !_tabs.Contains(tab)).ToList())
        {
            tab.PropertyChanged -= TabChanged;
            tab.Created -= TabCreatedAsync;
            _followed.Remove(tab);
        }
        foreach (var tab in _tabs.Where(tab => _followed.Add(tab)))
        {
            tab.PropertyChanged += TabChanged;
            tab.Created += TabCreatedAsync;
        }
        ShowTabs();
    }

    public void Activate(RequestTabViewModel? tab)
    {
        _selected = tab;
        Link();
        if (Flatten(Nodes).FirstOrDefault(node => node.IsActive) is { } row)
        {
            Reveal(row);
        }
    }

    public void ExpandTo(Guid? folder)
    {
        foreach (var above in Collection.FoldersDownTo(folder))
        {
            if (FolderRowOf(above.Id) is { } row)
            {
                row.IsExpanded = true;
            }
        }
    }

    void Reveal(RequestNodeViewModel row)
    {
        ExpandTo(row.ParentId);
        Revealed?.Invoke(row);
    }

    // Names need not be unique, but a copy gets a number that no request or draft in its folder has, so it can be told apart.
    public bool IsTaken(Guid? parent, string name) =>
        Collection.Requests.Any(request => Collection.FolderIdOf(request) == parent && SameName(request.Name, name))
        || _tabs.Any(tab => tab.IsDraft && tab.FolderId == parent && SameName(tab.Title, name));

    void TabChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RequestTabViewModel.Name))
        {
            Link();
            return;
        }
        if (args.PropertyName != nameof(RequestTabViewModel.IsDraft) || sender is not RequestTabViewModel { IsDraft: false } tab || !Flatten(Nodes).Any(node => node.IsDraft && node.Tab == tab))
        {
            return;
        }
        // A saved draft keeps its place in the tree.
        _loadVersion++;
        _order = [.. Flatten(Nodes).Select(node => node.IsDraft && node.Tab == tab ? $"{tab.Id}" : node.OrderKey)];
        snapshot.Current = Collection.With(tab.ToRequest() with { Method = tab.SavedMethod });
        Show();
        if (Flatten(Nodes).FirstOrDefault(node => node.Tab == tab) is { } row)
        {
            Reveal(row);
        }
    }

    Task TabCreatedAsync(RequestTabViewModel tab)
    {
        ResetOrderFailure();
        if (Collection.RequestOf(tab.Id) is null)
        {
            _loadVersion++;
            snapshot.Current = Collection.With(tab.ToRequest() with { Method = tab.SavedMethod });
            Show();
        }
        return SaveOrderAsync();
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var version = ++_loadVersion;
        try
        {
            var collection = await library.LoadAllAsync(cancellationToken);
            var order = (await LoadOrderAsync(cancellationToken)).Distinct().ToList();
            if (version != _loadVersion)
            {
                return;
            }
            snapshot.Current = collection;
            // Draft positions belong to this session, not the order file.
            foreach (var key in _order.Where(key => key.StartsWith('\0')))
            {
                var following = _order.SkipWhile(previous => previous != key).Skip(1).FirstOrDefault(order.Contains);
                order.Insert(following is null ? order.Count : order.IndexOf(following), key);
            }
            _order = order;
            Show();
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not list the requests");
        }
    }

    async Task<IReadOnlyList<string>> LoadOrderAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await library.LoadOrderAsync(cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read the request order; keeping the last known order");
            return [.. _order.Where(key => !key.StartsWith('\0'))];
        }
    }

    // Without a place in the order, folders come before requests, each by name.
    void Show()
    {
        var expanded = Flatten(Nodes).Where(node => node.IsFolder && node.IsExpanded).Select(node => node.Id).ToHashSet();
        var folders = Collection.Folders.OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(folder => folder.Id, folder => new RequestNodeViewModel(folder.Id, folder.Name, null, isFolder: true) { ParentId = Collection.ParentOf(folder), IsExpanded = expanded.Contains(folder.Id) });
        Nodes.Clear();
        foreach (var folder in folders.Values)
        {
            ChildrenOf(folder.ParentId).Add(folder);
        }
        foreach (var request in Collection.Requests.OrderBy(request => request.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var parent = Collection.FolderIdOf(request);
            ChildrenOf(parent).Add(new(request.Id, request.Name, request.Method, isFolder: false) { ParentId = parent });
        }
        // A file that cannot be read has no name or place, so it is shown at the top level by its id, and opening it tells what is wrong.
        foreach (var id in Collection.Unreadable)
        {
            Nodes.Add(new(id, translator.Format("Tree.Unreadable", $"{id}"[..8]), null, isFolder: false));
        }
        foreach (var tab in _tabs.Where(tab => tab.IsDraft))
        {
            ShowDraft(tab);
        }
        Sort(Nodes);
        Link();
        if (IsSearching)
        {
            Filter();
        }

        ObservableCollection<RequestNodeViewModel> ChildrenOf(Guid? parent) => parent is { } id && folders.TryGetValue(id, out var folder) ? folder.Children : Nodes;
    }

    void Sort(ObservableCollection<RequestNodeViewModel> nodes)
    {
        var positions = _order.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index);
        SortChildren(nodes);

        void SortChildren(ObservableCollection<RequestNodeViewModel> children)
        {
            var ordered = children.OrderBy(node => positions.GetValueOrDefault(node.OrderKey, int.MaxValue)).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                var current = children.IndexOf(ordered[index]);
                if (current != index)
                {
                    children.Move(current, index);
                }
                SortChildren(ordered[index].Children);
            }
        }
    }

    RequestNodeViewModel? FolderRowOf(Guid? id) => id is null ? null : Flatten(Nodes).FirstOrDefault(node => node.IsFolder && node.Id == id);

    // A draft whose folder is gone goes to the top.
    void ShowDraft(RequestTabViewModel tab)
    {
        if (tab.FolderId is not null && FolderRowOf(tab.FolderId) is null)
        {
            tab.MoveTo(null);
        }
        (FolderRowOf(tab.FolderId)?.Children ?? Nodes).Add(new(tab.Id, tab.Title, null, isFolder: false) { Tab = tab, IsDraft = true, ParentId = tab.FolderId });
    }

    void ShowTabs()
    {
        foreach (var row in Flatten(Nodes).Where(node => node.IsDraft && (node.Tab is not { IsDraft: true } tab || !_tabs.Contains(tab) || node.Id != tab.Id || node.ParentId != tab.FolderId)).ToList())
        {
            (FolderRowOf(row.ParentId)?.Children ?? Nodes).Remove(row);
        }
        foreach (var tab in _tabs.Where(tab => tab.IsDraft && !Flatten(Nodes).Any(node => node.IsDraft && node.Tab == tab)))
        {
            ShowDraft(tab);
        }
        Sort(Nodes);
        Link();
        if (IsSearching)
        {
            Filter();
        }
    }

    // The rows are only hidden, so the order and everything else that walks the tree still see them all.
    void Filter()
    {
        if (IsSearching)
        {
            _expandedBeforeSearch ??= [.. Flatten(Nodes).Where(node => node.IsFolder && node.IsExpanded).Select(node => node.Id)];
            foreach (var node in Nodes)
            {
                Mark(node, Search.Trim(), inShownFolder: false);
            }
        }
        else
        {
            foreach (var node in Flatten(Nodes))
            {
                node.IsShown = true;
                if (node.IsFolder && _expandedBeforeSearch is { } expanded)
                {
                    node.IsExpanded = expanded.Contains(node.Id);
                }
            }
            _expandedBeforeSearch = null;
        }
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(NothingFound));
    }

    // A row is shown when its name matches, when it lies in a folder that is shown whole, or when a row in it is shown. A shown folder is opened, so its rows can be seen.
    bool Mark(RequestNodeViewModel node, string text, bool inShownFolder)
    {
        var matches = (node.IsDraft ? node.Tab!.Title : node.Name).Contains(text, StringComparison.CurrentCultureIgnoreCase);
        var whole = inShownFolder || node.IsFolder && ShowWholeFolders && matches;
        var holdsShown = false;
        foreach (var child in node.Children)
        {
            holdsShown |= Mark(child, text, whole);
        }
        node.IsShown = whole || holdsShown || !node.IsFolder && matches;
        if (node.IsFolder)
        {
            node.IsExpanded = node.IsShown;
        }
        return node.IsShown;
    }

    void Link()
    {
        foreach (var row in Flatten(Nodes).Where(node => !node.IsFolder))
        {
            if (!row.IsDraft)
            {
                row.Tab = _tabs.FirstOrDefault(tab => tab.IsSaved && !tab.IsDraft && tab.Id == row.Id);
            }
            row.IsActive = row.Tab is not null && row.Tab == _selected;
        }
    }

    public static IEnumerable<RequestNodeViewModel> Flatten(IEnumerable<RequestNodeViewModel> nodes) => nodes.SelectMany(node => Flatten(node.Children).Prepend(node));

    static bool SameName(string name, string other) => string.Equals(name, other, StringComparison.OrdinalIgnoreCase);
}
