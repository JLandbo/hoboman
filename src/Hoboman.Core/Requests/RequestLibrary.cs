using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Requests;

public sealed class RequestLibrary(AppFolder folder, ILogger<RequestLibrary> logger)
{
    // Windows drops trailing dots and spaces from names, so such a name would not match the file it creates.
    public static bool IsValidName(string name) =>
        name.Split('/').All(part => part.Length > 0 && part is not ("." or "..") && !part.EndsWith('.') && !part.EndsWith(' ') && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

    public Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken) =>
        ListAsync(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Select(file => Path.ChangeExtension(file, null)), cancellationToken);

    public Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken) =>
        ListAsync(root => Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories), cancellationToken);

    public async Task<ApiRequest?> LoadAsync(string name, CancellationToken cancellationToken) => await FileOf(name).LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveAsync(string name, ApiRequest request, CancellationToken cancellationToken) => await FileOf(name).SaveAsync(request, cancellationToken).ConfigureAwait(false);

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

    Task<IReadOnlyList<string>> ListAsync(Func<string, IEnumerable<string>> list, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() =>
        Directory.Exists(folder.Requests) ? [.. list(folder.Requests).Select(path => Path.GetRelativePath(folder.Requests, path).Replace('\\', '/'))] : [],
        cancellationToken);

    JsonFile<ApiRequest?> FileOf(string name) => new(PathOf(name), null, logger);

    string PathOf(string name) => $"{FolderOf(name)}.json";

    string FolderOf(string name) => IsValidName(name) ? Path.Combine(folder.Requests, name) : throw new ArgumentException($"'{name}' is not a valid request name", nameof(name));
}
