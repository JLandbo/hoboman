using Hoboman.Core.Auth;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

public sealed class RequestLibrary(AppFolder folder, ILogger<RequestLibrary> logger)
{
    // The settings of a folder live inside it, so they move along when the folder is moved or renamed.
    const string _folderFile = ".folder.json";

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

    public async Task SaveAsync(string name, ApiRequest request, CancellationToken cancellationToken)
    {
        await FileOf(name).SaveAsync(request, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Saved the request {Name}", name);
    }

    public bool FolderExists(string name) => Directory.Exists(FolderOf(name));

    public async Task<FolderSettings?> LoadFolderAsync(string name, CancellationToken cancellationToken) => await FolderFileOf(name).LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveFolderAsync(string name, FolderSettings settings, CancellationToken cancellationToken) => await FolderFileOf(name).SaveAsync(settings, cancellationToken).ConfigureAwait(false);

    // A folder copied in Explorer takes its settings along, so two folders can share an id and with it their secrets.
    public async Task<bool> SharesFolderIdAsync(string name, Guid id, CancellationToken cancellationToken)
    {
        foreach (var other in await FoldersAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!other.Equals(name, StringComparison.OrdinalIgnoreCase) && (await LoadFolderAsync(other, cancellationToken).ConfigureAwait(false))?.Id == id)
            {
                return true;
            }
        }
        return false;
    }

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
                return new(settings.Id, settings.Auth);
            }
        }
        logger.LogDebug("{Name} inherits, but no folder above it has auth", name);
        return new(request.Id, AuthSettings.None);
    }

    public Task RenameAsync(string name, string newName, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var target = PathOf(newName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(PathOf(name), target);
        logger.LogInformation("Renamed the request {Name} to {NewName}", name, newName);
    }, cancellationToken);

    public Task DeleteAsync(string name, CancellationToken cancellationToken) => Task.Run(() =>
    {
        File.Delete(PathOf(name));
        logger.LogInformation("Deleted the request {Name}", name);
    }, cancellationToken);

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

    JsonFile<ApiRequest?> FileOf(string name) => new(PathOf(name), null, logger);

    JsonFile<FolderSettings?> FolderFileOf(string name) => new(Path.Combine(FolderOf(name), _folderFile), null, logger);

    string PathOf(string name) => $"{FolderOf(name)}.json";

    string FolderOf(string name) => IsValidName(name) ? Path.Combine(folder.Requests, name) : throw new ArgumentException($"'{name}' is not a valid request name", nameof(name));

    public static string? ParentOf(string? name) => name is not null && name.LastIndexOf('/') is var slash and > 0 ? name[..slash] : null;

    public static string LastPartOf(string name) => name[(name.LastIndexOf('/') + 1)..];
}
