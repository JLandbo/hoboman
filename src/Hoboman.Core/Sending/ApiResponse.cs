using System.Text.Json.Serialization;

namespace Hoboman.Core.Sending;

public sealed record ApiResponse(int StatusCode, string Reason, long ElapsedMs, long Size, IReadOnlyList<ResponseHeader> Headers, string Body)
{
    [JsonIgnore]
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
