using System.Text.Json.Serialization;

namespace Hoboman.Core.Sending;

public sealed record ApiResponse(int StatusCode, string Reason, long ElapsedMs, long Size, IReadOnlyList<ResponseHeader> Headers, string Body)
{
    [JsonIgnore]
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    // The body as the server sent it, so a file such as a PDF can be saved as it was. The history and the run logs keep only the text.
    [JsonIgnore]
    public byte[]? Bytes { get; init; }
}
