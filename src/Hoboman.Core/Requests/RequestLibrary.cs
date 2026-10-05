using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

// Each request and folder is a file named by its id, so names and places are fields: renaming or moving never moves a file.
public sealed class RequestLibrary(AppFolder folder, ILogger<RequestLibrary> logger)
{
    readonly JsonFile<IReadOnlyList<string>> _order = new(folder.RequestOrder, [], logger);

    public Task<IReadOnlyList<string>> LoadOrderAsync(CancellationToken cancellationToken) => _order.LoadAsync(cancellationToken);

    public Task SaveOrderAsync(IReadOnlyList<string> order, CancellationToken cancellationToken) => _order.SaveAsync(order, cancellationToken);

    // A name is only shown, so anything goes but nothing and line breaks, which a list of paths could not show.
    public static bool IsValidName(string name) => !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);

    // Only a name as the app writes it is an id, so a file named by the same id in another way is never a second file for it.
    // The parse skips spaces around an id, so the length rules them out.
    public static bool TryIdOf(string name, out Guid id) => Guid.TryParseExact(name, "D", out id) && name.Length == 36 && id != Guid.Empty;

    // A file whose name is not an id, such as a copy made in Explorer, is left out. A request that cannot be read is known by its id, so it can be shown.
    public async Task<RequestCollection> LoadAllAsync(CancellationToken cancellationToken)
    {
        var (folders, _) = await LoadEachAsync<RequestFolder>(folder.Folders, (item, id) => item with { Id = id }, cancellationToken).ConfigureAwait(false);
        var (requests, unreadable) = await LoadEachAsync<ApiRequest>(folder.Requests, (item, id) => item with { Id = id }, cancellationToken).ConfigureAwait(false);
        return new(folders, requests, unreadable);
    }

    // The ids that have a file, read from the names alone, so a file that cannot be read still counts.
    public Task<IReadOnlySet<Guid>> IdsAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlySet<Guid>>(() =>
        IdsIn(folder.Requests).Concat(IdsIn(folder.Folders)).Select(file => file.Id).ToHashSet(), cancellationToken);

    public bool Exists(Guid id) => File.Exists(RequestPathOf(id));

    public async Task<ApiRequest?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await RequestFileOf(id).LoadAsync(cancellationToken).ConfigureAwait(false) is { } request ? request with { Id = id } : null;

    public async Task SaveAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        await RequestFileOf(request.Id).SaveAsync(request, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the request {Name} ({Id})", request.Name, request.Id);
    }

    public Task CreateAsync(ApiRequest request, CancellationToken cancellationToken) => RequestFileOf(request.Id).SaveAsync(request, cancellationToken, overwrite: false);

    // Only the name or the place changes, so edits in an open tab are not saved along with it.
    public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken) => ChangeAsync(id, request => request with { Name = name }, cancellationToken);

    public Task MoveAsync(Guid id, Guid? folderId, CancellationToken cancellationToken) => ChangeAsync(id, request => request with { FolderId = folderId }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        File.Delete(RequestPathOf(id));
        logger.LogInformation("Deleted the request {Id}", id);
    }, logger, $"{id}", cancellationToken);

    public bool FolderExists(Guid id) => File.Exists(FolderPathOf(id));

    public async Task<RequestFolder?> LoadFolderAsync(Guid id, CancellationToken cancellationToken) =>
        await FolderFileOf(id).LoadAsync(cancellationToken).ConfigureAwait(false) is { } loaded ? loaded with { Id = id } : null;

    public async Task SaveFolderAsync(RequestFolder requestFolder, CancellationToken cancellationToken)
    {
        await FolderFileOf(requestFolder.Id).SaveAsync(requestFolder, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the folder {Name} ({Id})", requestFolder.Name, requestFolder.Id);
    }

    public Task CreateFolderAsync(RequestFolder requestFolder, CancellationToken cancellationToken) => FolderFileOf(requestFolder.Id).SaveAsync(requestFolder, cancellationToken, overwrite: false);

    public Task RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken) => ChangeFolderAsync(id, changed => changed with { Name = name }, cancellationToken);

    // The requests point at their folder, so moving a folder with everything in it changes one file.
    public Task MoveFolderAsync(Guid id, Guid? parentId, CancellationToken cancellationToken) => ChangeFolderAsync(id, changed => changed with { ParentId = parentId }, cancellationToken);

    public Task DeleteFolderAsync(Guid id, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        File.Delete(FolderPathOf(id));
        logger.LogInformation("Deleted the folder {Id}", id);
    }, logger, $"{id}", cancellationToken);

    // The nearest folder wins, like in Postman, so a subfolder can override the auth of the folder it is in.
    public async Task<AuthSource> AuthOfAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        if (request.Auth.Kind != AuthKind.Inherit)
        {
            return new(request.Id, request.Auth);
        }
        var loaded = new List<RequestFolder>();
        for (var id = request.FolderId; id is { } current && loaded.All(seen => seen.Id != current) && await LoadFolderAsync(current, cancellationToken).ConfigureAwait(false) is { } parent; id = parent.ParentId)
        {
            loaded.Add(parent);
        }
        // Only the folders the tree shows the request in count, so a loop of parents is cut as the tree cuts it.
        var collection = new RequestCollection(loaded, []);
        if (collection.FoldersDownTo(request.FolderId).LastOrDefault(parent => parent.Auth.Kind != AuthKind.Inherit) is { } nearest)
        {
            logger.LogDebug("{Name} uses the auth of the folder {Folder}", request.Name, nearest.Name);
            return new(nearest.Id, nearest.Auth, string.Join(" / ", collection.FoldersDownTo(nearest.Id).Select(parent => parent.Name)));
        }
        logger.LogDebug("{Name} inherits, but no folder above it has auth", request.Name);
        return new(request.Id, AuthSettings.None);
    }

    async Task ChangeAsync(Guid id, Func<ApiRequest, ApiRequest> change, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The request is gone.", RequestPathOf(id));
        await SaveAsync(change(request), cancellationToken).ConfigureAwait(false);
    }

    async Task ChangeFolderAsync(Guid id, Func<RequestFolder, RequestFolder> change, CancellationToken cancellationToken)
    {
        var loaded = await LoadFolderAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The folder is gone.", FolderPathOf(id));
        await SaveFolderAsync(change(loaded), cancellationToken).ConfigureAwait(false);
    }

    async Task<(IReadOnlyList<T> Loaded, IReadOnlyList<Guid> Unreadable)> LoadEachAsync<T>(string path, Func<T, Guid, T> withId, CancellationToken cancellationToken) where T : class
    {
        var files = await Task.Run(() => IdsIn(path).ToList(), cancellationToken).ConfigureAwait(false);
        var loaded = new T?[files.Count];
        var unreadable = new bool[files.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), cancellationToken, async (index, token) =>
        {
            try
            {
                loaded[index] = await new JsonFile<T?>(files[index].Path, null, logger).LoadAsync(token).ConfigureAwait(false) is { } item ? withId(item, files[index].Id) : null;
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Could not read {Path}", files[index].Path);
                unreadable[index] = true;
            }
        }).ConfigureAwait(false);
        return ([.. loaded.OfType<T>()], [.. files.Where((_, index) => unreadable[index]).Select(file => file.Id)]);
    }

    IEnumerable<(Guid Id, string Path)> IdsIn(string path)
    {
        if (!Directory.Exists(path))
        {
            yield break;
        }
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            if (TryIdOf(Path.GetFileNameWithoutExtension(file), out var id))
            {
                yield return (id, file);
            }
            else
            {
                logger.LogWarning("{Path} is left out, because its name is not an id", file);
            }
        }
    }

    JsonFile<ApiRequest?> RequestFileOf(Guid id) => new(RequestPathOf(id), null, logger);

    JsonFile<RequestFolder?> FolderFileOf(Guid id) => new(FolderPathOf(id), null, logger);

    string RequestPathOf(Guid id) => Path.Combine(folder.Requests, $"{id}.json");

    string FolderPathOf(Guid id) => Path.Combine(folder.Folders, $"{id}.json");
}
