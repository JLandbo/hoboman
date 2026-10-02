namespace Hoboman.Core.Settings;

public sealed record TabSession(IReadOnlyList<string> Requests, string? Selected = null);
