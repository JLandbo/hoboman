namespace Hoboman.Cli;

// Without a call, the newest calls are listed, as many as Count.
sealed record HistoryInput(string? Call, int Count)
{
    public const int DefaultCount = 20;

    // A call is given by its name, or the newest are listed, as many as asked for.
    public static bool IsValid(string? call, int count, bool countGiven) => count >= 1 && (call is null || !countGiven);
}
