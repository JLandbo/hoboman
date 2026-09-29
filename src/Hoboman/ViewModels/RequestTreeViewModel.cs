using System.Collections.ObjectModel;
using Hoboman.Core.Requests;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class RequestTreeViewModel(RequestLibrary library, ILogger<RequestTreeViewModel> logger)
{
    IReadOnlyDictionary<Guid, string> _nameById = new Dictionary<Guid, string>();

    public ObservableCollection<RequestNodeViewModel> Nodes { get; } = [];

    // Moving a file shows up as a delete and a create, so open tabs find their file again by its id.
    public string? NameOf(Guid id) => _nameById.GetValueOrDefault(id);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var folders = await library.FoldersAsync(cancellationToken);
            var names = await library.NamesAsync(cancellationToken);
            var requests = new ApiRequest?[names.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, names.Count), cancellationToken, async (index, token) => requests[index] = await RequestOfAsync(names[index], token));
            Show(folders, names, requests);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not list the requests");
        }
    }

    void Show(IEnumerable<string> folders, IReadOnlyList<string> names, IReadOnlyList<ApiRequest?> requests)
    {
        var expanded = Flatten(Nodes).Where(node => node.IsExpanded).Select(node => node.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, RequestNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        Nodes.Clear();
        foreach (var folder in folders.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            FolderOf(folder);
        }
        foreach (var (name, request) in names.Zip(requests).OrderBy(pair => pair.First, StringComparer.CurrentCultureIgnoreCase))
        {
            ChildrenOf(name).Add(new(name, request?.Method, isFolder: false));
        }
        _nameById = UniqueIds(names, requests);

        ObservableCollection<RequestNodeViewModel> ChildrenOf(string path) => path.LastIndexOf('/') is var slash and > 0 ? FolderOf(path[..slash]).Children : Nodes;

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

    static IEnumerable<RequestNodeViewModel> Flatten(IEnumerable<RequestNodeViewModel> nodes) => nodes.SelectMany(node => Flatten(node.Children).Prepend(node));
}
