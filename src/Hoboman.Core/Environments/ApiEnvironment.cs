using System.Text.RegularExpressions;
using Hoboman.Core.Requests;

namespace Hoboman.Core.Environments;

public sealed partial record ApiEnvironment(string Name, IReadOnlyList<KeyValue> Variables)
{
    // Without a chosen environment nothing is resolved, and secrets kept per environment go under the empty name.
    public static ApiEnvironment None { get; } = new("", []);

    public static MatchCollection VariablesIn(string text) => Variable().Matches(text);

    public static string WithVariablesAs(string text, string value) => Variable().Replace(text, value);

    public string Resolve(string text) =>
        Variable().Replace(text, match => Variables.FirstOrDefault(variable => variable.Enabled && variable.Name == match.Groups[1].Value)?.Value ?? match.Value);

    [GeneratedRegex(@"\{\{([^{}]+)\}\}")]
    private static partial Regex Variable();
}
