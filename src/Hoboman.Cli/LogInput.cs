namespace Hoboman.Cli;

// Without a run and without Last, the runs are listed.
sealed record LogInput(string Workflow, string? Run, bool Last, bool Follow);
