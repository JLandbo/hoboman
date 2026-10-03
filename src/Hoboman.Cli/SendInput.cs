namespace Hoboman.Cli;

// One target is a saved request, and two are a method and a URL.
sealed record SendInput(string[] Target, string? EnvironmentName, string[] Headers, string? JsonBody, string? TextBody, string[] Variables, string? VariablesFile)
{
    public bool IsDirect => Target.Length == 2;
}
