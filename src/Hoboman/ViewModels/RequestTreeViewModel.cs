using System.Collections.ObjectModel;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class RequestTreeViewModel(RequestLibrary library, ILogger<RequestTreeViewModel> logger)
{
    public ObservableCollection<RequestNodeViewModel> Nodes { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var folders = await library.FoldersAsync(cancellationToken);
            var names = await library.NamesAsync(cancellationToken);
            var methods = new string?[names.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, names.Count), cancellationToken, async (index, token) => methods[index] = await MethodOfAsync(names[index], token));
            Show(folders, names.Zip(methods));
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not list the requests");
        }
    }

    void Show(IEnumerable<string> folders, IEnumerable<(string Name, string? Method)> requests)
    {
        var expanded = Flatten(Nodes).Where(node => node.IsExpanded).Select(node => node.Path).ToHashSet();
        var byPath = new Dictionary<string, RequestNodeViewModel>();
        Nodes.Clear();
        foreach (var folder in folders.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var node = new RequestNodeViewModel(folder, null, isFolder: true) { IsExpanded = expanded.Contains(folder) };
            ChildrenOf(folder).Add(node);
            byPath[folder] = node;
        }
        foreach (var (name, method) in requests.OrderBy(request => request.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ChildrenOf(name).Add(new(name, method, isFolder: false));
        }

        ObservableCollection<RequestNodeViewModel> ChildrenOf(string path) => path.LastIndexOf('/') is var slash and > 0 ? byPath[path[..slash]].Children : Nodes;
    }

    async Task<string?> MethodOfAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var request = await library.LoadAsync(name, cancellationToken);
            if (request?.Id == Guid.Empty)
            {
                logger.LogInformation("{Name} has no id", name);
            }
            return request?.Method;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read {Name}", name);
            return null;
        }
    }

    static IEnumerable<RequestNodeViewModel> Flatten(IEnumerable<RequestNodeViewModel> nodes) => nodes.SelectMany(node => Flatten(node.Children).Prepend(node));
}

public sealed class RequestNodeViewModel(string path, string? method, bool isFolder) : ObservableObject
{
    public string Path => path;

    public string Name => path[(path.LastIndexOf('/') + 1)..];

    public string? Method => method;

    public bool IsFolder => isFolder;

    public bool IsExpanded { get; set => Set(ref field, value); }

    public ObservableCollection<RequestNodeViewModel> Children { get; } = [];
}
