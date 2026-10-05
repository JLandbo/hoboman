using System.Net.Http.Headers;

namespace Hoboman.Core.Sending;

// The type and subtype of a Content-Type, such as application and problem+json, without its parameters and in lower case.
public readonly record struct MediaType(string Type, string Subtype)
{
    public static MediaType? Of(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.MediaType?.ToLowerInvariant().Split('/') is [var type, var subtype] ? new(type, subtype) : null;

    // The format itself or a suffix of it, as RFC 6838 names them, so application/problem+json is JSON and application/x-ndjson is not.
    public bool Is(string format) => Subtype == format || Subtype.EndsWith($"+{format}", StringComparison.Ordinal);
}
