using System.CommandLine;

namespace Hoboman.Cli;

sealed class CommandLine
{
    readonly RootCommand _rootCommand = new("Send Hoboman requests without opening the GUI.");
    readonly Command _listCommand = new("list", "List saved request paths.");
    readonly Command _sendCommand = new("send", "Send a saved request or a method and URL.");
    readonly Argument<string[]> _targetArgument = new("request-or-method-url") { Arity = new(1, 2) };
    readonly Option<string> _environmentOption = new("--env") { Description = "Select an environment." };
    readonly Option<string[]> _headersOption = new("-H") { Description = "Add a direct request header: Name: Value." };
    readonly Option<string> _jsonOption = new("--json") { Description = "Direct JSON body: text, @file or @@literal." };
    readonly Option<string> _textOption = new("--text") { Description = "Direct text body: text, @file or @@literal." };
    readonly Option<string[]> _variablesOption = new("--var") { Description = "Set a temporary variable: name=value." };
    readonly Option<string> _variablesFileOption = new("--vars") { Description = "Read string variables from a JSON file or redirected stdin (-)." };

    public CommandLine()
    {
        _rootCommand.Subcommands.Add(_listCommand);
        _rootCommand.Subcommands.Add(_sendCommand);
        _sendCommand.Arguments.Add(_targetArgument);
        foreach (var option in new Option[] { _environmentOption, _headersOption, _jsonOption, _textOption, _variablesOption, _variablesFileOption })
        {
            _sendCommand.Options.Add(option);
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
            return new(IsList: true);
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
        return new(Send: new(targets, result.GetValue(_environmentOption), result.GetValue(_headersOption) ?? [], jsonBody, textBody, result.GetValue(_variablesOption) ?? [], result.GetValue(_variablesFileOption)));
    }

    static CommandInput Invalid() => new(Problem: "Invalid command arguments. Use --help for usage.");
}
