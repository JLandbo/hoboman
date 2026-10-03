using System.Collections.ObjectModel;
using System.ComponentModel;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class RequestTreeViewModel(RequestLibrary library, IDialogs dialogs, Translator translator, ILogger<RequestTreeViewModel> logger)
{
    IReadOnlyDictionary<Guid, string> _nameById = new Dictionary<Guid, string>();
    Dictionary<string, Guid> _idByName = new(StringComparer.OrdinalIgnoreCase);
    IReadOnlySet<string> _names = new HashSet<string>();
    IReadOnlySet<string> _earlierNames = new HashSet<string>();
    IReadOnlyList<string> _folders = [];
    IReadOnlyList<(string Name, string? Method)> _files = [];
    IReadOnlyList<string> _order = [];
    ObservableCollection<RequestTabViewModel> _tabs = [];
    readonly HashSet<RequestTabViewModel> _followed = [];
    RequestTabViewModel? _selected;
    int _loadVersion;
    bool _orderSaveFailed;

    public ObservableCollection<RequestNodeViewModel> Nodes { get; } = [];

    public event Action<RequestNodeViewModel>? Revealed;

    internal void ResetOrderFailure() => _orderSaveFailed = false;

    public Task SaveOrderAsync()
    {
        _loadVersion++;
        _order = [.. Flatten(Nodes).Select(node => node.OrderKey)];
        return PersistOrderAsync();
    }

    public Task RenamedAsync(RequestNodeViewModel node, string name)
    {
        _loadVersion++;
        _order = [.. Flatten(Nodes).Select(row => row.OrderKey).Select(key => (node.IsFolder ? key.StartsWith($"{node.Path}/", StringComparison.OrdinalIgnoreCase) : key.Equals(node.Path, StringComparison.OrdinalIgnoreCase)) ? $"{name}{key[node.Path.Length..]}" : key)];
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

    public async Task PlaceAsync(string key, string? parent, string? relativeTo, DropPosition position)
    {
        var siblings = FolderRowOf(parent)?.Children ?? Nodes;
        if (siblings.FirstOrDefault(node => node.OrderKey.Equals(key, StringComparison.OrdinalIgnoreCase)) is not { } moved)
        {
            return;
        }
        var ordered = siblings.Where(node => node != moved).ToList();
        var target = ordered.FindIndex(node => node.OrderKey.Equals(relativeTo, StringComparison.OrdinalIgnoreCase));
        var index = target < 0 ? ordered.Count : target + (position == DropPosition.After ? 1 : 0);
        siblings.Move(siblings.IndexOf(moved), index);
        await SaveOrderAsync();
        Reveal(moved);
    }

    public void RefreshDrafts() => Show();

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
        _order = [.. Flatten(Nodes).Select(node => node.IsDraft && node.Tab == tab ? name : node.OrderKey)];
        _idByName[name] = tab.Id;
        _files = [.. _files.Where(file => !string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)), (name, tab.SavedMethod)];
        Show();
        if (Flatten(Nodes).FirstOrDefault(node => node.Tab == tab) is { } row)
        {
            Reveal(row);
        }
    }

    Task TabCreatedAsync(RequestTabViewModel tab)
    {
        ResetOrderFailure();
        if (tab.Name is { } name && !_files.Any(file => file.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _loadVersion++;
            _idByName[name] = tab.Id;
            _files = [.. _files, (name, tab.SavedMethod)];
            Show();
        }
        return SaveOrderAsync();
    }

    // Moving a file shows up as a delete and a create, so open tabs find their file again by its id, under a name that is new since the last load.
    // A copy that is left with the id after its original is deleted was there before, so it is not taken for a move.
    public string? NameOf(Guid id) => _nameById.GetValueOrDefault(id) is { } name && !_earlierNames.Contains(name) ? name : null;

    public Guid IdOf(string name) => _idByName.GetValueOrDefault(name);

    public string KeyOf(string name) => Flatten(Nodes).FirstOrDefault(node => !node.IsFolder && !node.IsDraft && node.Path.Equals(name, StringComparison.OrdinalIgnoreCase))?.OrderKey ?? name;

    // Where the request with the id is now. An id that several files share points at none of them.
    public string? PathOf(Guid id) => _nameById.GetValueOrDefault(id);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var version = ++_loadVersion;
        try
        {
            var folders = await library.FoldersAsync(cancellationToken);
            var names = await library.NamesAsync(cancellationToken);
            var loaded = (await LoadOrderAsync(cancellationToken)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var requests = new ApiRequest?[names.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, names.Count), cancellationToken, async (index, token) => requests[index] = await RequestOfAsync(names[index], token));
            if (version != _loadVersion)
            {
                return;
            }
            var namesById = names.Zip(requests).Where(pair => pair.Second is { Id: var id } && id != Guid.Empty).ToLookup(pair => pair.Second!.Id, pair => pair.First);
            _nameById = UniqueIds(namesById);
            (_earlierNames, _names) = (_names, names.ToHashSet(StringComparer.OrdinalIgnoreCase));
            _idByName = names.Zip(requests).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => pair.Second!.Id, StringComparer.OrdinalIgnoreCase);
            // An id that an Explorer copy now shares is replaced by the paths of its files, so the original keeps its place.
            var order = loaded.SelectMany(key => Guid.TryParse(key, out var id) && namesById[id].Skip(1).Any() ? namesById[id] : [IdKeyOf(key) is var known && known != Guid.Empty ? $"{known}" : key])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var mapped = !order.SequenceEqual(loaded, StringComparer.OrdinalIgnoreCase);
            _folders = folders;
            _files = [.. names.Zip(requests).Select(pair => (pair.First, pair.Second?.Method))];
            // Draft positions belong to this session, not the order file.
            foreach (var key in _order.Where(key => key.StartsWith('\0')))
            {
                var following = _order.SkipWhile(previous => previous != key).Skip(1).FirstOrDefault(order.Contains);
                order.Insert(following is null ? order.Count : order.IndexOf(following), key);
            }
            _order = order;
            Show();
            if (mapped)
            {
                await PersistOrderAsync();
            }
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
            ChildrenOf(name).Add(new(name, method, isFolder: false, IdKeyOf(name)));
        }
        foreach (var tab in _tabs.Where(tab => tab.IsDraft))
        {
            ShowDraft(tab);
        }
        Sort(Nodes);
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

    void Sort(ObservableCollection<RequestNodeViewModel> nodes)
    {
        var positions = _order.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index, StringComparer.OrdinalIgnoreCase);
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
        foreach (var row in Flatten(Nodes).Where(node => node.IsDraft && (node.Tab is not { IsDraft: true } || !_tabs.Contains(node.Tab) || node.Path != node.Tab.DraftName)).ToList())
        {
            (FolderRowOf(RequestLibrary.ParentOf(row.Path))?.Children ?? Nodes).Remove(row);
        }
        foreach (var tab in _tabs.Where(tab => tab.IsDraft && !Flatten(Nodes).Any(node => node.IsDraft && node.Tab == tab)))
        {
            ShowDraft(tab);
        }
        Sort(Nodes);
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

    IReadOnlyDictionary<Guid, string> UniqueIds(ILookup<Guid, string> namesById)
    {
        var nameById = new Dictionary<Guid, string>();
        foreach (var group in namesById)
        {
            if (group.Count() == 1)
            {
                nameById[group.Key] = group.First();
                continue;
            }
            logger.LogWarning("{Names} share the id {Id}, so they share their secrets", string.Join(", ", group), group.Key);
        }
        return nameById;
    }

    // A tab saved under a new name keeps its id until the next load, while the file that had it may still be there, so the id must point at this file.
    Guid IdKeyOf(string name) => _idByName.GetValueOrDefault(name) is var id && _nameById.GetValueOrDefault(id) is { } path && path.Equals(name, StringComparison.OrdinalIgnoreCase) ? id : Guid.Empty;

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
