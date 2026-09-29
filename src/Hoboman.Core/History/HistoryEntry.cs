using Hoboman.Core.Requests;
using Hoboman.Core.Sending;

namespace Hoboman.Core.History;

public enum HistorySource { App, Cli }

public sealed record HistoryEntry(DateTimeOffset At, HistorySource Source, string? Name, string? EnvironmentName, string Address, ApiRequest Request, ApiResponse? Response, string? Error);
