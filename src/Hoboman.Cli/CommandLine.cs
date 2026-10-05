using System.CommandLine;

namespace Hoboman.Cli;

sealed class CommandLine
{
    readonly RootCommand _rootCommand = new("Send Hoboman requests and run workflows without opening the GUI.");
    readonly Command _listCommand = new("list", "List saved requests as id, a tab and path, or the same for 'workflows' and 'folders'.");
    readonly Command _newCommand = new("new", "Make a saved request, folder or workflow.");
    readonly Command _newRequestCommand = new("request", "Make a request in a folder, given by id or path, or . for the top.");
    readonly Command _newFolderCommand = new("folder", "Make a folder in a folder, given by id or path, or . for the top.");
    readonly Command _newWorkflowCommand = new("workflow", "Make an empty workflow.");
    readonly Command _renameCommand = new("rename", "Rename a saved request, folder or workflow, given by id, path or name.");
    readonly Command _moveCommand = new("move", "Move a saved request or folder into a folder, or . for the top.");
    readonly Command _deleteCommand = new("delete", "Delete a saved request, a folder with all in it, or a workflow. Needs --yes.");
    readonly Command _sendCommand = new("send", "Send a saved request or a method and URL.");
    readonly Command _runCommand = new("run", "Run a workflow by its id or name.");
    readonly Argument<string[]> _targetArgument = new("request-or-method-url") { Arity = new(1, 2) };
    readonly Argument<string> _workflowArgument = new("workflow");
    readonly Argument<string> _listArgument = new("workflows|folders") { Arity = ArgumentArity.ZeroOrOne };
    readonly Argument<string> _folderArgument = new("folder");
    readonly Argument<string> _nameArgument = new("name");
    readonly Argument<string> _changedArgument = new("target");
    readonly Option<string> _methodOption = new("--method") { Description = "The method of a new request. GET when not given." };
    readonly Option<string> _urlOption = new("--url") { Description = "The URL of a new request." };
    readonly Option<bool> _yesOption = new("--yes") { Description = "Delete without asking." };
    readonly Option<string> _environmentOption = new("--env") { Description = "Select an environment." };
    readonly Option<string[]> _headersOption = new("-H") { Description = "Add a direct request header: Name: Value." };
    readonly Option<string> _jsonOption = new("--json") { Description = "Direct JSON body: text, @file or @@literal." };
    readonly Option<string> _textOption = new("--text") { Description = "Direct text body: text, @file or @@literal." };
    readonly Option<string> _outOption = new("--out") { Description = "Write the response body to a file, byte for byte." };
    readonly Option<string[]> _variablesOption = new("--var") { Description = "Set a temporary variable: name=value." };
    readonly Option<string> _variablesFileOption = new("--vars") { Description = "Read string variables from a JSON file or redirected stdin (-)." };
    readonly Option<string[]> _parametersOption = new("--param") { Description = "Give a parameter as text: name=value." };
    readonly Option<string> _parametersFileOption = new("--params") { Description = "Read typed parameters from a JSON file or redirected stdin (-)." };

    public CommandLine()
    {
        _rootCommand.Subcommands.Add(_listCommand);
        _listArgument.AcceptOnlyFromAmong("workflows", "folders");
        _listCommand.Arguments.Add(_listArgument);
        _rootCommand.Subcommands.Add(_sendCommand);
        _rootCommand.Subcommands.Add(_runCommand);
        _sendCommand.Arguments.Add(_targetArgument);
        foreach (var option in new Option[] { _environmentOption, _headersOption, _jsonOption, _textOption, _outOption, _variablesOption, _variablesFileOption })
        {
            _sendCommand.Options.Add(option);
        }
        _runCommand.Arguments.Add(_workflowArgument);
        foreach (var option in new Option[] { _environmentOption, _parametersOption, _parametersFileOption })
        {
            _runCommand.Options.Add(option);
        }
        _rootCommand.Subcommands.Add(_newCommand);
        _newCommand.Subcommands.Add(_newRequestCommand);
        _newCommand.Subcommands.Add(_newFolderCommand);
        _newCommand.Subcommands.Add(_newWorkflowCommand);
        _newRequestCommand.Arguments.Add(_folderArgument);
        _newRequestCommand.Arguments.Add(_nameArgument);
        foreach (var option in new Option[] { _methodOption, _urlOption, _headersOption, _jsonOption, _textOption })
        {
            _newRequestCommand.Options.Add(option);
        }
        _newFolderCommand.Arguments.Add(_folderArgument);
        _newFolderCommand.Arguments.Add(_nameArgument);
        _newWorkflowCommand.Arguments.Add(_nameArgument);
        _rootCommand.Subcommands.Add(_renameCommand);
        _renameCommand.Arguments.Add(_changedArgument);
        _renameCommand.Arguments.Add(_nameArgument);
        _rootCommand.Subcommands.Add(_moveCommand);
        _moveCommand.Arguments.Add(_changedArgument);
        _moveCommand.Arguments.Add(_folderArgument);
        _rootCommand.Subcommands.Add(_deleteCommand);
        _deleteCommand.Arguments.Add(_changedArgument);
        _deleteCommand.Options.Add(_yesOption);
    }

    public CommandInput Parse(string[] arguments)
    {
        // @file is a body to send, so it must not be read as a file of more arguments.
        var result = _rootCommand.Parse(arguments, new() { ResponseFileTokenReplacer = null });
        if (result.Errors.Count > 0)
        {
            return Invalid();
        }
        // Only the built-in help and version have an action.
        if (result.Action is not null)
        {
            return new(StandardOutput: result);
        }
        if (result.CommandResult.Command == _listCommand)
        {
            return new(IsList: true, ListsWorkflows: result.GetValue(_listArgument) == "workflows", ListsFolders: result.GetValue(_listArgument) == "folders");
        }
        if (result.CommandResult.Command == _runCommand)
        {
            return new(Run: new(result.GetValue(_workflowArgument)!, result.GetValue(_environmentOption), result.GetValue(_parametersOption) ?? [], result.GetValue(_parametersFileOption)));
        }
        var command = result.CommandResult.Command;
        if (command == _newRequestCommand || command == _newFolderCommand || command == _newWorkflowCommand)
        {
            var json = result.GetValue(_jsonOption);
            var text = result.GetValue(_textOption);
            return json is not null && text is not null ? Invalid() : new(New: new(command == _newRequestCommand ? SavedKind.Request : command == _newFolderCommand ? SavedKind.Folder : SavedKind.Workflow, command == _newWorkflowCommand ? null : result.GetValue(_folderArgument), result.GetValue(_nameArgument)!,
                result.GetValue(_methodOption) ?? "GET", result.GetValue(_urlOption) ?? "", result.GetValue(_headersOption) ?? [], json, text));
        }
        if (command == _renameCommand || command == _moveCommand || command == _deleteCommand)
        {
            return new(Change: new(command == _renameCommand ? ChangeKind.Rename : command == _moveCommand ? ChangeKind.Move : ChangeKind.Delete, result.GetValue(_changedArgument)!, command == _renameCommand ? result.GetValue(_nameArgument) : command == _moveCommand ? result.GetValue(_folderArgument) : null, result.GetValue(_yesOption)));
        }
        if (command != _sendCommand)
        {
            return Invalid();
        }
        var targets = result.GetValue(_targetArgument)!;
        var jsonBody = result.GetValue(_jsonOption);
        var textBody = result.GetValue(_textOption);
        if (jsonBody is not null && textBody is not null)
        {
            return Invalid();
        }
        // A saved request is sent with its own headers and body.
        if (targets.Length == 1 && (jsonBody is not null || textBody is not null || result.GetResult(_headersOption) is { Implicit: false }))
        {
            return Invalid();
        }
        return new(Send: new(targets, result.GetValue(_environmentOption), result.GetValue(_headersOption) ?? [], jsonBody, textBody, result.GetValue(_outOption), result.GetValue(_variablesOption) ?? [], result.GetValue(_variablesFileOption)));
    }

    static CommandInput Invalid() => new(Problem: "Invalid command arguments. Use --help for usage.");
}
