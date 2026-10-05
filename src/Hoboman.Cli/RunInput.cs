namespace Hoboman.Cli;

// The workflow is its id or name.
sealed record RunInput(string Workflow, string? EnvironmentName, string[] Parameters, string? ParametersFile);
