using System.CommandLine;

namespace Hoboman.Cli;

sealed record CommandInput(SendInput? Send = null, RunInput? Run = null, bool IsList = false, bool ListsWorkflows = false, ParseResult? StandardOutput = null, string? Problem = null,
    bool ListsFolders = false, NewInput? New = null, ChangeInput? Change = null, bool ListsEnvironments = false, string? Show = null, UpdateInput? Update = null, LogInput? Log = null, HistoryInput? History = null, bool IsMcp = false);
