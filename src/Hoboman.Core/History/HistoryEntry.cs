using Hoboman.Core.Requests;
using Hoboman.Core.Sending;

namespace Hoboman.Core.History;

// Problem is the kind of what went wrong, so the app can tell it in its language. Calls kept before it have only the Error.
public sealed record HistoryEntry(DateTimeOffset At, HistorySource Source, string Address, ApiRequest Request, string? Name = null, string? EnvironmentName = null, ApiResponse? Response = null, string? Error = null,
    RequestProblemKind? Problem = null);
