namespace Hoboman.Tests.Requests;

// Tests place requests and folders by a path such as "Shop/Login", as it is short to read. The app keeps names and places in fields.
static class LibraryPaths
{
    public static async Task<ApiRequest> SaveAtAsync(this RequestLibrary library, string path, ApiRequest request, CancellationToken cancellationToken = default)
    {
        var parts = path.Split('/');
        var saved = request with { Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id, Name = parts[^1], FolderId = await library.FolderAtAsync(string.Join('/', parts[..^1]), cancellationToken) };
        await library.SaveAsync(saved, cancellationToken);
        return saved;
    }

    // The folders on the way are made when they are not there.
    public static async Task<Guid?> FolderAtAsync(this RequestLibrary library, string path, CancellationToken cancellationToken = default)
    {
        Guid? parent = null;
        foreach (var name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var folders = (await library.LoadAllAsync(cancellationToken)).Folders;
            var folder = folders.FirstOrDefault(folder => folder.ParentId == parent && folder.Name == name);
            if (folder is null)
            {
                folder = new() { Id = Guid.NewGuid(), Name = name, ParentId = parent };
                await library.SaveFolderAsync(folder, cancellationToken);
            }
            parent = folder.Id;
        }
        return parent;
    }

    // The folder at the path takes the given id, or keeps its own when none is given.
    public static async Task<RequestFolder> SaveFolderAtAsync(this RequestLibrary library, string path, RequestFolder folder, CancellationToken cancellationToken = default)
    {
        var parts = path.Split('/');
        var parent = await library.FolderAtAsync(string.Join('/', parts[..^1]), cancellationToken);
        var existing = (await library.LoadAllAsync(cancellationToken)).Folders.FirstOrDefault(other => other.ParentId == parent && other.Name == parts[^1]);
        var saved = folder with { Id = folder.Id != Guid.Empty ? folder.Id : existing?.Id ?? Guid.NewGuid(), Name = parts[^1], ParentId = parent };
        await library.SaveFolderAsync(saved, cancellationToken);
        return saved;
    }

    public static async Task<RequestFolder?> LoadFolderAtAsync(this RequestLibrary library, string path, CancellationToken cancellationToken = default)
    {
        var collection = await library.LoadAllAsync(cancellationToken);
        return collection.Folders.SingleOrDefault(folder => collection.FolderPathOf(folder.Id) == path);
    }

    // Changes made outside the app, as another program or an agent makes them.
    public static async Task DeleteAtAsync(this RequestLibrary library, string path, CancellationToken cancellationToken = default) =>
        await library.DeleteAsync((await library.LoadAtAsync(path, cancellationToken))!.Id, cancellationToken);

    public static async Task DeleteFolderAtAsync(this RequestLibrary library, string path, CancellationToken cancellationToken = default) =>
        await library.DeleteFolderAsync((await library.LoadFolderAtAsync(path, cancellationToken))!.Id, cancellationToken);

    public static async Task RenameAtAsync(this RequestLibrary library, string path, string newPath, CancellationToken cancellationToken = default)
    {
        var request = (await library.LoadAtAsync(path, cancellationToken))!;
        var parts = newPath.Split('/');
        await library.SaveAsync(request with { Name = parts[^1], FolderId = await library.FolderAtAsync(string.Join('/', parts[..^1]), cancellationToken) }, cancellationToken);
    }

    public static async Task RenameFolderAtAsync(this RequestLibrary library, string path, string newPath, CancellationToken cancellationToken = default)
    {
        var folder = (await library.LoadFolderAtAsync(path, cancellationToken))!;
        var parts = newPath.Split('/');
        await library.SaveFolderAsync(folder with { Name = parts[^1], ParentId = await library.FolderAtAsync(string.Join('/', parts[..^1]), cancellationToken) }, cancellationToken);
    }

    // The order is kept by id. A folder is written with a slash after it, as "Folder/", and a key that is no path is kept as it is.
    public static async Task SaveOrderAtAsync(this RequestLibrary library, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var collection = await library.LoadAllAsync(cancellationToken);
        await library.SaveOrderAsync([.. paths.Select(path => path.EndsWith('/')
            ? collection.Folders.FirstOrDefault(folder => collection.FolderPathOf(folder.Id) == path[..^1])?.Id.ToString() ?? path
            : collection.Find(path).FirstOrDefault()?.Id.ToString() ?? path)], cancellationToken);
    }

    public static async Task<IReadOnlyList<string>> LoadOrderAtAsync(this RequestLibrary library, CancellationToken cancellationToken = default)
    {
        var collection = await library.LoadAllAsync(cancellationToken);
        return [.. (await library.LoadOrderAsync(cancellationToken)).Select(key => !Guid.TryParse(key, out var id) ? key
            : collection.FolderOf(id) is not null ? $"{collection.FolderPathOf(id)}/"
            : collection.RequestOf(id) is { } request ? collection.PathOf(request) : key)];
    }

    // Off the caller's thread, so a UI test can wait for it without waiting on itself.
    public static bool ExistsAt(this RequestLibrary library, string path) => Task.Run(() => library.LoadAtAsync(path)).GetAwaiter().GetResult() is not null;

    public static bool FolderExistsAt(this RequestLibrary library, string path) => Task.Run(() => library.LoadFolderAtAsync(path)).GetAwaiter().GetResult() is not null;

    public static async Task<ApiRequest?> LoadAtAsync(this RequestLibrary library, string path, CancellationToken cancellationToken = default) =>
        (await library.LoadAllAsync(cancellationToken)).Find(path).SingleOrDefault();

    public static async Task<IReadOnlyList<string>> PathsAsync(this RequestLibrary library, CancellationToken cancellationToken = default)
    {
        var collection = await library.LoadAllAsync(cancellationToken);
        return [.. collection.Requests.Select(collection.PathOf).Order(StringComparer.Ordinal)];
    }
}
