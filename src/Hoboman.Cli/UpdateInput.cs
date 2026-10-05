namespace Hoboman.Cli;

// The file is a path, or - for redirected stdin.
sealed record UpdateInput(string Target, string File);
