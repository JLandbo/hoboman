namespace Hoboman.Core.Requests;

// The folders and requests as last read, kept in one place, so the tree and the tabs look names and places up in the same copy without reading the files.
public sealed class RequestSnapshot
{
    public RequestCollection Current { get; set; } = RequestCollection.Empty;
}
