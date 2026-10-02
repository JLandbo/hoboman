using System.Text;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;

namespace Hoboman.ViewModels;

// Lays out a JSON or XML request body the way a response is shown, or gives null when it is not what its kind says.
// When variables are enabled, placeholders preserve them during formatting; their values are filled in only when sending.
public static class BodyLayout
{
    public static string? Of(string body, BodyKind kind, bool useVariables = false) => kind switch
    {
        BodyKind.Json => useVariables ? JsonOf(body) : ResponseDisplay.PrettyJsonOf(body),
        BodyKind.Xml => useVariables ? XmlOf(body) : ResponseDisplay.PrettyXmlOf(body),
        _ => null,
    };

    // A body such as 1.{{decimal}} is only valid once its variables are filled in, so it cannot be laid out while they stand in it, and still is no invalid body.
    public static bool NeedsVariables(string body, BodyKind kind, ApiEnvironment environment) =>
        ApiEnvironment.VariablesIn(body).Count > 0 && Of(environment.Resolve(body), kind) is not null;

    // A variable outside quotes, such as {{count}}, is not JSON, so a text of its own stands in for it. One inside quotes is JSON already.
    static string? JsonOf(string body)
    {
        var prefix = StandInPrefix();
        string StandIn(int index) => $"\"{prefix}{index}\"";
        var loose = new List<string>();
        var text = new StringBuilder(body.Length);
        var inText = false;
        for (var at = 0; at < body.Length; at++)
        {
            if (!inText && ApiEnvironment.VariableAt(body, at) is { } variable)
            {
                text.Append(StandIn(IndexOf(loose, variable)));
                at += variable.Length - 1;
                continue;
            }
            var symbol = body[at];
            text.Append(symbol);
            if (inText && symbol == '\\' && at + 1 < body.Length)
            {
                text.Append(body[++at]);
            }
            else if (symbol == '"')
            {
                inText = !inText;
            }
        }
        return ResponseDisplay.PrettyJsonOf(text.ToString()) is { } laidOut ? PutBack(laidOut, loose, StandIn) : null;
    }

    // A variable can stand where XML takes only a name, such as <{{root}}>, so a name of its own stands in for each.
    // It ends in a letter, so the one for the first variable is not also the start of the one for the tenth.
    static string? XmlOf(string body)
    {
        var prefix = StandInPrefix();
        string StandIn(int index) => $"{prefix}{index}e";
        var variables = new List<string>();
        var text = ApiEnvironment.WithVariablesAs(body, variable => StandIn(IndexOf(variables, variable)));
        return ResponseDisplay.PrettyXmlOf(text) is { } laidOut ? PutBack(laidOut, variables, StandIn) : null;
    }

    // The same variable gets the same stand-in, so <{{root}}> still ends with </{{root}}>.
    static int IndexOf(List<string> variables, string variable)
    {
        if (variables.IndexOf(variable) is var index and >= 0)
        {
            return index;
        }
        variables.Add(variable);
        return variables.Count - 1;
    }

    // Starts with a letter and holds only letters and digits, so it fits wherever a name does.
    static string StandInPrefix() => $"hoboman{Guid.NewGuid():N}v";

    static string PutBack(string laidOut, IReadOnlyList<string> variables, Func<int, string> standIn)
    {
        for (var index = 0; index < variables.Count; index++)
        {
            laidOut = laidOut.Replace(standIn(index), variables[index], StringComparison.Ordinal);
        }
        return laidOut;
    }
}
