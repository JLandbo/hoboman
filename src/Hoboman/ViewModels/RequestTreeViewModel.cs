using System.Collections.ObjectModel;
using System.ComponentModel;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class RequestTreeViewModel(RequestLibrary library, ILogger<RequestTreeViewModel> logger)
{
    IReadOnlyDictionary<Guid, string> _nameById = new Dictionary<Guid, string>();
    Dictionary<string, Guid> _idByName = new(StringComparer.OrdinalIgnoreCase);
    IReadOnlySet<string> _names = new HashSet<string>();
    IReadOnlySet<string> _earlierNames = new HashSet<string>();
    IReadOnlyList<string> _folders = [];
    IReadOnlyList<(string Name, string? Method)> _files = [];
    ObservableCollection<RequestTabViewModel> _tabs = [];
    readonly HashSet<RequestTabViewModel> _followed = [];
    RequestTabViewModel? _selected;
    int _loadVersion;

    public ObservableCollection<RequestNodeViewModel> Nodes { get; } = [];

    public event Action<RequestNodeViewModel>? Revealed;

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
            _followed.Remove(tab);
        }
        foreach (var tab in _tabs.Where(tab => _followed.Add(tab)))
        {
            tab.PropertyChanged += TabChanged;
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

    public void ExpandTo(string path)
    {
        for (var parent = RequestLibrary.ParentOf(path); parent is not null; parent = RequestLibrary.ParentOf(parent))
        {
            if (FolderRowOf(parent) is { } folder)
            {
                folder.IsExpanded = true;
            }
        }
    }

    void Reveal(RequestNodeViewModel row)
    {
        ExpandTo(row.Path);
        Revealed?.Invoke(row);
    }

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
        if (tab.Name is not { } name)
        {
            ShowTabs();
            return;
        }
        _loadVersion++;
        _idByName[name] = tab.Id;
        _files = [.. _files.Where(file => !string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)), (name, tab.SavedMethod)];
        Show();
        if (Flatten(Nodes).FirstOrDefault(node => node.Tab == tab) is { } row)
        {
            Reveal(row);
        }
    }

    // Moving a file shows up as a delete and a create, so open tabs find their file again by its id, under a name that is new since the last load.
    // A copy that is left with the id after its original is deleted was there before, so it is not taken for a move.
    public string? NameOf(Guid id) => _nameById.GetValueOrDefault(id) is { } name && !_earlierNames.Contains(name) ? name : null;

    public Guid IdOf(string name) => _idByName.GetValueOrDefault(name);

    public bool IsUsed(Guid id) => _idByName.Values.Contains(id);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var version = ++_loadVersion;
        try
        {
            var folders = await library.FoldersAsync(cancellationToken);
            var names = await library.NamesAsync(cancellationToken);
            var requests = new ApiRequest?[names.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, names.Count), cancellationToken, async (index, token) => requests[index] = await RequestOfAsync(names[index], token));
            if (version != _loadVersion)
            {
                return;
            }
            _nameById = UniqueIds(names, requests);
            (_earlierNames, _names) = (_names, names.ToHashSet(StringComparer.OrdinalIgnoreCase));
            _idByName = names.Zip(requests).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => pair.Second!.Id, StringComparer.OrdinalIgnoreCase);
            _folders = folders;
            _files = [.. names.Zip(requests).Select(pair => (pair.First, pair.Second?.Method))];
            Show();
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not list the requests");
        }
    }

    void Show()
    {
        var expanded = Flatten(Nodes).Where(node => node.IsExpanded).Select(node => node.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, RequestNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        Nodes.Clear();
        foreach (var folder in _folders.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            FolderOf(folder);
        }
        foreach (var (name, method) in _files.OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ChildrenOf(name).Add(new(name, method, isFolder: false));
        }
        foreach (var tab in _tabs.Where(tab => tab.IsDraft))
        {
            ShowDraft(tab);
        }
        Link();

        ObservableCollection<RequestNodeViewModel> ChildrenOf(string path) => RequestLibrary.ParentOf(path) is { } parent ? FolderOf(parent).Children : Nodes;

        // A folder can appear between listing the folders and the files, so missing parents are made on the way.
        RequestNodeViewModel FolderOf(string path)
        {
            if (!byPath.TryGetValue(path, out var node))
            {
                node = new(path, null, isFolder: true) { IsExpanded = expanded.Contains(path) };
                ChildrenOf(path).Add(node);
                byPath[path] = node;
            }
            return node;
        }
    }

    RequestNodeViewModel? FolderRowOf(string? path) => Flatten(Nodes).FirstOrDefault(node => node.IsFolder && string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase));

    void ShowDraft(RequestTabViewModel tab)
    {
        var destination = tab.Destination;
        while (destination is not null && FolderRowOf(destination) is null)
        {
            destination = RequestLibrary.ParentOf(destination);
        }
        if (destination != tab.Destination)
        {
            tab.MoveTo(destination);
        }
        (FolderRowOf(destination)?.Children ?? Nodes).Add(new(tab.DraftName!, null, isFolder: false) { Tab = tab, IsDraft = true });
    }

    void ShowTabs()
    {
        foreach (var row in Flatten(Nodes).Where(node => node.IsDraft && (node.Tab is not { IsDraft: true } || !_tabs.Contains(node.Tab))).ToList())
        {
            (FolderRowOf(RequestLibrary.ParentOf(row.Path))?.Children ?? Nodes).Remove(row);
        }
        foreach (var tab in _tabs.Where(tab => tab.IsDraft && !Flatten(Nodes).Any(node => node.IsDraft && node.Tab == tab)))
        {
            ShowDraft(tab);
        }
        Link();
    }

    void Link()
    {
        foreach (var row in Flatten(Nodes).Where(node => !node.IsFolder))
        {
            if (!row.IsDraft)
            {
                row.Tab = _tabs.FirstOrDefault(tab => !tab.IsDraft && string.Equals(tab.Name, row.Path, StringComparison.OrdinalIgnoreCase));
            }
            row.IsActive = row.Tab is not null && row.Tab == _selected;
        }
    }

    IReadOnlyDictionary<Guid, string> UniqueIds(IReadOnlyList<string> names, IReadOnlyList<ApiRequest?> requests)
    {
        var nameById = new Dictionary<Guid, string>();
        foreach (var group in names.Zip(requests).Where(pair => pair.Second is { Id: var id } && id != Guid.Empty).GroupBy(pair => pair.Second!.Id))
        {
            if (group.Count() == 1)
            {
                nameById[group.Key] = group.First().First;
                continue;
            }
            logger.LogWarning("{Names} share the id {Id}, so they share their secrets", string.Join(", ", group.Select(pair => pair.First)), group.Key);
        }
        return nameById;
    }

    async Task<ApiRequest?> RequestOfAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var request = await library.LoadAsync(name, cancellationToken);
            if (request?.Id == Guid.Empty)
            {
                logger.LogInformation("{Name} has no id", name);
            }
            return request;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read {Name}", name);
            return null;
        }
    }

    public static IEnumerable<RequestNodeViewModel> Flatten(IEnumerable<RequestNodeViewModel> nodes) => nodes.SelectMany(node => Flatten(node.Children).Prepend(node));
}
