using Hoboman.Core.Requests;

namespace Hoboman.Core.Sending;

public sealed record ApiResponse(int StatusCode, string Reason, TimeSpan Elapsed, long Size, IReadOnlyList<KeyValue> Headers, string Body);
