using System.CommandLine;

namespace Hoboman.Cli;

sealed class CommandLine
{
    readonly RootCommand _rootCommand = new("Send Hoboman requests and run workflows without opening the GUI.");
    readonly Command _listCommand = new("list", "List saved request paths, or workflow names with 'workflows'.");
    readonly Command _sendCommand = new("send", "Send a saved request or a method and URL.");
    readonly Command _runCommand = new("run", "Run a workflow by its folder name.");
    readonly Argument<string[]> _targetArgument = new("request-or-method-url") { Arity = new(1, 2) };
    readonly Argument<string> _workflowArgument = new("workflow");
    readonly Argument<string> _listArgument = new("workflows") { Arity = ArgumentArity.ZeroOrOne };
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
        _listArgument.AcceptOnlyFromAmong("workflows");
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
            return new(IsList: true, ListsWorkflows: result.GetValue(_listArgument) is not null);
        }
        if (result.CommandResult.Command == _runCommand)
        {
            return new(Run: new(result.GetValue(_workflowArgument)!, result.GetValue(_environmentOption), result.GetValue(_parametersOption) ?? [], result.GetValue(_parametersFileOption)));
        }
        if (result.CommandResult.Command != _sendCommand)
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
