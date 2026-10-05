namespace Hoboman.Cli;

enum SavedKind { Request, Folder, Workflow }

// What new makes: a request or a folder in a folder, or a workflow, which lies in no folder.
sealed record NewInput(SavedKind Kind, string? Folder, string Name, string Method, string Url, string[] Headers, string? JsonBody, string? TextBody);
