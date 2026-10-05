using System.CommandLine;

namespace Hoboman.Cli;

sealed class CommandLine
{
    readonly RootCommand _rootCommand = new("Send Hoboman requests, run workflows and read and change what Hoboman saves, without opening the GUI.");
    readonly Command _listCommand = new("list", "List saved requests as id, a tab and path, or the same for 'workflows', 'folders' and 'environments'.");
    readonly Command _newCommand = new("new", "Make a saved request, folder, workflow or environment.");
    readonly Command _newRequestCommand = new("request", "Make a request in a folder, given by id or path, or . for the top.");
    readonly Command _newFolderCommand = new("folder", "Make a folder in a folder, given by id or path, or . for the top.");
    readonly Command _newWorkflowCommand = new("workflow", "Make an empty workflow.");
    readonly Command _newEnvironmentCommand = new("environment", "Make an empty environment.");
    readonly Command _renameCommand = new("rename", "Rename a saved request, folder, workflow or environment, given by id, path or name.");
    readonly Command _moveCommand = new("move", "Move a saved request or folder into a folder, or . for the top.");
    readonly Command _deleteCommand = new("delete", "Delete a saved request, a folder with all in it, a workflow or an environment. Needs --yes.");
    readonly Command _sendCommand = new("send", "Send a saved request or a method and URL.");
    readonly Command _runCommand = new("run", "Run a workflow by its id or name.");
    readonly Command _checkCommand = new("check", "Check a workflow as run does, without sending anything.");
    readonly Command _showCommand = new("show", "Write a saved request, folder, workflow or environment as JSON, as update takes it.");
    readonly Command _updateCommand = new("update", "Replace what a saved request, workflow or environment holds with JSON as show writes it.");
    readonly Command _logCommand = new("log", "List the runs of a workflow, or write the events of one, given by its run id or --last.");
    readonly Command _historyCommand = new("history", "List the newest calls in the history, or write one, given by its name, as JSON.");
    readonly Argument<string[]> _targetArgument = new("request-or-method-url") { Arity = new(1, 2) };
    readonly Argument<string> _workflowArgument = new("workflow");
    readonly Argument<string> _listArgument = new("workflows|folders|environments") { Arity = ArgumentArity.ZeroOrOne };
    readonly Argument<string> _runArgument = new("run") { Arity = ArgumentArity.ZeroOrOne };
    readonly Argument<string> _callArgument = new("call") { Arity = ArgumentArity.ZeroOrOne };
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
    readonly Option<string> _fileOption = new("--file") { Description = "Read the JSON from a file or redirected stdin (-).", Required = true };
    readonly Option<bool> _lastOption = new("--last") { Description = "The newest run." };
    readonly Option<bool> _followOption = new("--follow") { Description = "Wait for new events until the run has finished." };
    readonly Option<int> _countOption = new("--count") { Description = "How many calls to list. 20 when not given.", DefaultValueFactory = _ => 20 };

    public CommandLine()
    {
        _rootCommand.Subcommands.Add(_listCommand);
        _listArgument.AcceptOnlyFromAmong("workflows", "folders", "environments");
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
        _newCommand.Subcommands.Add(_newEnvironmentCommand);
        _newEnvironmentCommand.Arguments.Add(_nameArgument);
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
        _rootCommand.Subcommands.Add(_checkCommand);
        _checkCommand.Arguments.Add(_workflowArgument);
        foreach (var option in new Option[] { _environmentOption, _parametersOption, _parametersFileOption })
        {
            _checkCommand.Options.Add(option);
        }
        _rootCommand.Subcommands.Add(_showCommand);
        _showCommand.Arguments.Add(_changedArgument);
        _rootCommand.Subcommands.Add(_updateCommand);
        _updateCommand.Arguments.Add(_changedArgument);
        _updateCommand.Options.Add(_fileOption);
        _rootCommand.Subcommands.Add(_logCommand);
        _logCommand.Arguments.Add(_workflowArgument);
        _logCommand.Arguments.Add(_runArgument);
        _logCommand.Options.Add(_lastOption);
        _logCommand.Options.Add(_followOption);
        _rootCommand.Subcommands.Add(_historyCommand);
        _historyCommand.Arguments.Add(_callArgument);
        _historyCommand.Options.Add(_countOption);
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
            return new(IsList: true, ListsWorkflows: result.GetValue(_listArgument) == "workflows", ListsFolders: result.GetValue(_listArgument) == "folders",
                ListsEnvironments: result.GetValue(_listArgument) == "environments");
        }
        var command = result.CommandResult.Command;
        if (command == _runCommand || command == _checkCommand)
        {
            return new(Run: new(result.GetValue(_workflowArgument)!, result.GetValue(_environmentOption), result.GetValue(_parametersOption) ?? [], result.GetValue(_parametersFileOption),
                CheckOnly: command == _checkCommand));
        }
        if (command == _showCommand)
        {
            return new(Show: result.GetValue(_changedArgument));
        }
        if (command == _updateCommand)
        {
            return new(Update: new(result.GetValue(_changedArgument)!, result.GetValue(_fileOption)!));
        }
        // A call is given by its name, or the newest are listed, as many as asked for.
        if (command == _historyCommand)
        {
            var call = result.GetValue(_callArgument);
            var count = result.GetValue(_countOption);
            return count < 1 || call is not null && result.GetResult(_countOption) is { Implicit: false } ? Invalid() : new(History: new(call, count));
        }
        // A run is given by its id or as the newest, not both, and only one run can be followed.
        if (command == _logCommand)
        {
            var run = result.GetValue(_runArgument);
            var last = result.GetValue(_lastOption);
            var follow = result.GetValue(_followOption);
            return run is not null && last || follow && run is null && !last ? Invalid() : new(Log: new(result.GetValue(_workflowArgument)!, run, last, follow));
        }
        if (command == _newRequestCommand || command == _newFolderCommand || command == _newWorkflowCommand || command == _newEnvironmentCommand)
        {
            var json = result.GetValue(_jsonOption);
            var text = result.GetValue(_textOption);
            var kind = command == _newRequestCommand ? SavedKind.Request : command == _newFolderCommand ? SavedKind.Folder : command == _newWorkflowCommand ? SavedKind.Workflow : SavedKind.Environment;
            return json is not null && text is not null ? Invalid() : new(New: new(kind, kind is SavedKind.Request or SavedKind.Folder ? result.GetValue(_folderArgument) : null, result.GetValue(_nameArgument)!,
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
