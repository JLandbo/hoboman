namespace Hoboman.Cli;

enum ChangeKind { Rename, Move, Delete }

// rename gives the new name and move the folder in Value, and delete only goes ahead with Yes.
sealed record ChangeInput(ChangeKind Command, string Target, string? Value, bool Yes);
