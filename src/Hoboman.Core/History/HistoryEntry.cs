using Hoboman.Core.Requests;
using Hoboman.Core.Sending;

namespace Hoboman.Core.History;

public sealed record HistoryEntry(DateTimeOffset At, HistorySource Source, string Address, ApiRequest Request, string? Name = null, string? EnvironmentName = null, ApiResponse? Response = null, string? Error = null);
