namespace Hoboman.Cli;

// Without a call, the newest calls are listed, as many as Count.
sealed record HistoryInput(string? Call, int Count);
