using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

public sealed class RequestLibrary(AppFolder folder, ILogger<RequestLibrary> logger)
{
    // The settings of a folder live inside it, so they move along when the folder is moved or renamed.
    const string _folderFile = ".folder.json";

    readonly JsonFile<IReadOnlyList<string>> _order = new(folder.RequestOrder, [], logger);

    public Task<IReadOnlyList<string>> LoadOrderAsync(CancellationToken cancellationToken) => _order.LoadAsync(cancellationToken);

    public Task SaveOrderAsync(IReadOnlyList<string> order, CancellationToken cancellationToken) => _order.SaveAsync(order, cancellationToken);

    // Windows drops trailing dots and spaces from names, so such a name would not match the file it creates.
    // Names starting with a dot are kept for Hoboman's own files, such as the folder settings.
    public static bool IsValidName(string name) =>
        name.Split('/').All(part => part.Length > 0 && !part.StartsWith('.') && !part.EndsWith('.') && !part.EndsWith(' ') && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

    public Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken) =>
        ListAsync(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).Equals(_folderFile, StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.ChangeExtension(file, null)), cancellationToken);

    public Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken) =>
        ListAsync(root => Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories), cancellationToken);

    public bool Exists(string name) => File.Exists(PathOf(name));

    // These are async, so an invalid name fails the returned task instead of throwing before there is one.
    public async Task<ApiRequest?> LoadAsync(string name, CancellationToken cancellationToken) => await FileOf(name).LoadAsync(cancellationToken).ConfigureAwait(false);

    // A file that cannot be read is given without a request, so one broken file does not stop the use of the rest.
    public async Task<IReadOnlyList<(string Name, ApiRequest? Request)>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var names = await NamesAsync(cancellationToken).ConfigureAwait(false);
        var requests = new ApiRequest?[names.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, names.Count), cancellationToken, async (index, token) => requests[index] = await TryLoadAsync(names[index], token).ConfigureAwait(false))
            .ConfigureAwait(false);
        return [.. names.Zip(requests)];
    }

    public async Task SaveAsync(string name, ApiRequest request, CancellationToken cancellationToken)
    {
        await FileOf(name).SaveAsync(request, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the request {Name}", name);
    }

    public Task CreateAsync(string name, ApiRequest request, CancellationToken cancellationToken) => FileOf(name).SaveAsync(request, cancellationToken, overwrite: false);

    public bool FolderExists(string name) => Directory.Exists(FolderOf(name));

    public async Task<FolderSettings?> LoadFolderAsync(string name, CancellationToken cancellationToken) => await FolderFileOf(name).LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveFolderAsync(string name, FolderSettings settings, CancellationToken cancellationToken, bool createDirectory = true) => await FolderFileOf(name).SaveAsync(settings, cancellationToken, createDirectory).ConfigureAwait(false);

    public Task<bool> SharesFolderIdAsync(string name, Guid id, CancellationToken cancellationToken) =>
        SharesIdAsync(FoldersAsync, async (other, token) => (await LoadFolderAsync(other, token).ConfigureAwait(false))?.Id, name, id, cancellationToken);

    public Task<bool> SharesRequestIdAsync(string name, Guid id, CancellationToken cancellationToken) =>
        SharesIdAsync(NamesAsync, async (other, token) => (await LoadAsync(other, token).ConfigureAwait(false))?.Id, name, id, cancellationToken);

    // The nearest folder wins, like in Postman, so a subfolder can override the auth of the folder it is in.
    public async Task<AuthSource> AuthOfAsync(string? name, ApiRequest request, CancellationToken cancellationToken)
    {
        if (request.Auth.Kind != AuthKind.Inherit)
        {
            return new(request.Id, request.Auth);
        }
        for (var parent = ParentOf(name); parent is not null; parent = ParentOf(parent))
        {
            if (await LoadFolderAsync(parent, cancellationToken).ConfigureAwait(false) is { Auth.Kind: not AuthKind.Inherit } settings)
            {
                logger.LogDebug("{Name} uses the auth of the folder {Folder}", name, parent);
                return new(settings.Id, settings.Auth, parent);
            }
        }
        logger.LogDebug("{Name} inherits, but no folder above it has auth", name);
        return new(request.Id, AuthSettings.None);
    }

    public Task RenameAsync(string name, string newName, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        var target = PathOf(newName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(PathOf(name), target);
        logger.LogInformation("Renamed the request {Name} to {NewName}", name, newName);
    }, logger, name, cancellationToken);

    public Task DeleteAsync(string name, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        File.Delete(PathOf(name));
        logger.LogInformation("Deleted the request {Name}", name);
    }, logger, name, cancellationToken);

    // The folder's settings are inside it, so they move with it.
    public Task RenameFolderAsync(string name, string newName, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        var target = FolderOf(newName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(FolderOf(name), target);
        logger.LogInformation("Renamed the folder {Name} to {NewName}", name, newName);
    }, logger, name, cancellationToken);

    public Task DeleteFolderAsync(string name, CancellationToken cancellationToken) => Retrying.RunAsync(() =>
    {
        Directory.Delete(FolderOf(name), recursive: true);
        logger.LogInformation("Deleted the folder {Name} with everything in it", name);
    }, logger, name, cancellationToken);

    public Task CreateFolderAsync(string name, CancellationToken cancellationToken) => Task.Run(() =>
    {
        Directory.CreateDirectory(FolderOf(name));
        logger.LogInformation("Created the folder {Name}", name);
    }, cancellationToken);

    // Cut the root off by hand, because Path.GetRelativePath trims trailing spaces and dots and would give names that do not match the files.
    Task<IReadOnlyList<string>> ListAsync(Func<string, IEnumerable<string>> list, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() =>
    {
        if (!Directory.Exists(folder.Requests))
        {
            return [];
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Requests)) + Path.DirectorySeparatorChar;
        return [.. list(root).Select(path => path[root.Length..].Replace('\\', '/')).Where(IsUsable)];
    }, cancellationToken);

    bool IsUsable(string name)
    {
        if (IsValidName(name))
        {
            return true;
        }
        logger.LogWarning("{Name} is left out, because it cannot be used as a request name", name);
        return false;
    }

    // A request or folder copied in Explorer takes its id along, and with it the secrets saved under that id.
    async Task<bool> SharesIdAsync(Func<CancellationToken, Task<IReadOnlyList<string>>> list, Func<string, CancellationToken, Task<Guid?>> idOf, string name, Guid id, CancellationToken cancellationToken)
    {
        foreach (var other in await list(cancellationToken).ConfigureAwait(false))
        {
            if (other.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                if (await idOf(other, cancellationToken).ConfigureAwait(false) == id)
                {
                    return true;
                }
            }
            catch (Exception exception) when (FileProblem.Is(exception))
            {
                logger.LogWarning(exception, "Skipped {Name} while looking for a shared id", other);
            }
        }
        return false;
    }

    async Task<ApiRequest?> TryLoadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogWarning(exception, "Could not read {Name}", name);
            return null;
        }
    }

    JsonFile<ApiRequest?> FileOf(string name) => new(PathOf(name), null, logger);

    JsonFile<FolderSettings?> FolderFileOf(string name) => new(Path.Combine(FolderOf(name), _folderFile), null, logger);

    string PathOf(string name) => $"{FolderOf(name)}.json";

    string FolderOf(string name) => IsValidName(name) ? Path.Combine(folder.Requests, name) : throw new ArgumentException($"'{name}' is not a valid request name", nameof(name));

    public static string? ParentOf(string? name) => name is not null && name.LastIndexOf('/') is var slash and > 0 ? name[..slash] : null;

    public static string LastPartOf(string name) => name[(name.LastIndexOf('/') + 1)..];
}
