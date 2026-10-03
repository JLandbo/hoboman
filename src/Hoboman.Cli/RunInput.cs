namespace Hoboman.Cli;

// The workflow is its folder name.
sealed record RunInput(string Workflow, string? EnvironmentName, string[] Parameters, string? ParametersFile);
