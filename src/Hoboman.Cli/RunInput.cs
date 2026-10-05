namespace Hoboman.Cli;

// The workflow is its id or name. A check is a run that stops before it sends anything.
sealed record RunInput(string Workflow, string? EnvironmentName, string[] Parameters, string? ParametersFile, bool CheckOnly = false);
