using System.Text.RegularExpressions;
using Hoboman.Core.Requests;

namespace Hoboman.Core.Environments;

public sealed partial record ApiEnvironment(string Name, IReadOnlyList<KeyValue> Variables)
{
    public string Resolve(string text) =>
        Variable().Replace(text, match => Variables.FirstOrDefault(variable => variable.Enabled && variable.Name == match.Groups[1].Value)?.Value ?? match.Value);

    [GeneratedRegex(@"\{\{([^{}]+)\}\}")]
    private static partial Regex Variable();
}
