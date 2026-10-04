using System.CommandLine;

namespace Hoboman.Cli;

sealed record CommandInput(SendInput? Send = null, RunInput? Run = null, bool IsList = false, bool ListsWorkflows = false, ParseResult? StandardOutput = null, string? Problem = null);
