namespace Hoboman.Tests.ViewModels;

// Tests find rows by a path such as "Shop/Login", as it is short to read. The tree knows rows by id.
static class TreePaths
{
    public static string PathOf(this RequestTreeViewModel tree, RequestNodeViewModel node)
    {
        var folders = RequestTreeViewModel.Flatten(tree.Nodes).Where(row => row.IsFolder).ToDictionary(row => row.Id);
        var names = new List<string> { node.Name };
        for (var parent = node.ParentId; parent is { } id && folders.TryGetValue(id, out var folder); parent = folder.ParentId)
        {
            names.Insert(0, folder.Name);
        }
        return string.Join('/', names);
    }

    public static RequestNodeViewModel NodeAt(this RequestTreeViewModel tree, string path) => RequestTreeViewModel.Flatten(tree.Nodes).Single(node => tree.PathOf(node) == path);

    public static RequestNodeViewModel NodeOf(this RequestTreeViewModel tree, RequestTabViewModel tab) => RequestTreeViewModel.Flatten(tree.Nodes).Single(node => node.Tab == tab);

    // A folder's path, such as "Users/Admin", or null at the top.
    public static string? PathOfFolder(this RequestTreeViewModel tree, Guid? folder) => folder is null ? null : tree.Collection.FolderPathOf(folder);

    public static string ParentPath(string path) => path[..path.LastIndexOf('/')];

    // The path a saved tab's request has, such as "Folder/Request".
    public static string? PathOf(this RequestTreeViewModel tree, RequestTabViewModel tab) => tab.Name is { } name ? tree.Collection.PathOf(tab.ToRequest() with { Name = name }) : null;
}
