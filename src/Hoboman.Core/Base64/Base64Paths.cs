namespace Hoboman.Core.Base64;

// The properties sent as Base64 and those shown decoded in the response, kept as JSON paths in the request's file, so they can be edited there too.
public sealed record Base64Paths
{
    public IReadOnlyList<string> Encode { get; init; } = [];

    public IReadOnlyList<string> Decode { get; init; } = [];
}
