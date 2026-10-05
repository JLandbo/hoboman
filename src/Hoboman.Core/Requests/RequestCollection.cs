namespace Hoboman.Core.Requests;

// All folders and requests, as their files say.
public sealed class RequestCollection(IReadOnlyList<RequestFolder> folders, IReadOnlyList<ApiRequest> requests, IReadOnlyList<Guid>? unreadable = null)
{
    readonly Dictionary<Guid, RequestFolder> _folders = folders.ToDictionary(folder => folder.Id);

    public static RequestCollection Empty { get; } = new([], []);

    public IReadOnlyList<RequestFolder> Folders => folders;

    public IReadOnlyList<ApiRequest> Requests => requests;

    // The requests whose files cannot be read, so they have no name or place.
    public IReadOnlyList<Guid> Unreadable => unreadable ?? [];

    public RequestFolder? FolderOf(Guid? id) => id is { } known ? _folders.GetValueOrDefault(known) : null;

    public ApiRequest? RequestOf(Guid id) => requests.FirstOrDefault(request => request.Id == id);

    // The folders from the top down to the one given, as the tree shows them. A folder whose parent is gone is at the top,
    // and so is each folder in a loop of parents, so deleting one never takes the others in the loop along.
    public IReadOnlyList<RequestFolder> FoldersDownTo(Guid? id)
    {
        var path = new List<RequestFolder>();
        for (var folder = FolderOf(id); folder is not null; folder = FolderOf(folder.ParentId))
        {
            if (path.FindIndex(below => below.Id == folder.Id) is var loop and >= 0)
            {
                path.RemoveRange(0, loop);
                break;
            }
            path.Insert(0, folder);
        }
        return path;
    }

    public Guid? ParentOf(RequestFolder folder) => FoldersDownTo(folder.Id) is [.., var parent, _] ? parent.Id : null;

    public Guid? FolderIdOf(ApiRequest request) => FolderOf(request.FolderId)?.Id;

    // The path the CLI and the history show, such as "Shop/Login". A name can hold a slash, so a path is for reading and finding, not for splitting.
    public string PathOf(ApiRequest request) => string.Join("/", FoldersDownTo(request.FolderId).Select(folder => folder.Name).Append(request.Name));

    public string FolderPathOf(Guid? id) => string.Join("/", FoldersDownTo(id).Select(folder => folder.Name));

    public IReadOnlyList<ApiRequest> Find(string path) => [.. requests.Where(request => PathOf(request).Equals(path, StringComparison.OrdinalIgnoreCase))];

    // A request saved in the app is shown before the files are read again.
    public RequestCollection With(ApiRequest request) => new(folders, [.. requests.Where(other => other.Id != request.Id), request], [.. Unreadable.Where(id => id != request.Id)]);

    // The folder and every folder below it.
    public IReadOnlySet<Guid> FoldersIn(Guid id) => folders.Where(folder => FoldersDownTo(folder.Id).Any(above => above.Id == id)).Select(folder => folder.Id).ToHashSet();
}
