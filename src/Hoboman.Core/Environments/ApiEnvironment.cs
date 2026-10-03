using System.Text.RegularExpressions;
using Hoboman.Core.Requests;

namespace Hoboman.Core.Environments;

public sealed partial record ApiEnvironment(string Name, IReadOnlyList<KeyValue> Variables)
{
    // Without a chosen environment nothing is resolved, and secrets kept per environment go under the empty name.
    public static ApiEnvironment None { get; } = new("", []);

    public static MatchCollection VariablesIn(string text) => Variable().Matches(text);

    const string _variable = @"\{\{([^{}]+)\}\}";

    public static string WithVariablesAs(string text, Func<string, string> standIn) => Variable().Replace(text, match => standIn(match.Value));

    // Only one that starts right there, so looking for it does not search the rest of the text.
    public static string? VariableAt(string text, int offset) => VariableHere().Match(text, offset) is { Success: true } match ? match.Value : null;

    public string Resolve(string text) =>
        Variable().Replace(text, match => Variables.FirstOrDefault(variable => variable.Enabled && variable.Name == match.Groups[1].Value)?.Value ?? match.Value);

    public ApiEnvironment WithVariables(IReadOnlyList<KeyValue> variables) => new(Name, [.. variables, .. Variables]);

    [GeneratedRegex(_variable)]
    private static partial Regex Variable();

    [GeneratedRegex(@"\G" + _variable)]
    private static partial Regex VariableHere();
}
