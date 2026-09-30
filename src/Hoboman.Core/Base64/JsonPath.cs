using System.Globalization;
using System.Text;

namespace Hoboman.Core.Base64;

// A small part of JSONPath (RFC 9535), enough to point at properties: "$" is the whole body, ".name" or "['name']" a property,
// and "[*]" every element of a list. A place found in a body names its element instead, as "[0]".
public static class JsonPath
{
    public const string Root = "$";

    public static string Member(string path, string name) =>
        IsPlain(name) ? $"{path}.{name}" : $"{path}['{name.Replace(@"\", @"\\").Replace("'", @"\'")}']";

    public static string Each(string path) => $"{path}[*]";

    public static string Element(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");

    public static bool IsInside(string path, string container) =>
        path.Length > container.Length && path.StartsWith(container, StringComparison.Ordinal) && path[container.Length] is '.' or '[';

    // The names of the properties on the way, with null for every element of a list, or null when the text is not such a path.
    public static IReadOnlyList<string?>? StepsOf(string path)
    {
        if (!path.StartsWith(Root, StringComparison.Ordinal))
        {
            return null;
        }
        var steps = new List<string?>();
        for (var at = Root.Length; at < path.Length;)
        {
            if (path[at] == '.')
            {
                var end = at + 1;
                while (end < path.Length && IsNameCharacter(path[end]))
                {
                    end++;
                }
                if (end == at + 1)
                {
                    return null;
                }
                steps.Add(path[(at + 1)..end]);
                at = end;
            }
            else if (path.AsSpan(at).StartsWith("[*]"))
            {
                steps.Add(null);
                at += 3;
            }
            else if (path.AsSpan(at).StartsWith("['") && QuotedName(path, at + 2) is (var quoted, var next))
            {
                steps.Add(quoted);
                at = next;
            }
            else
            {
                return null;
            }
        }
        return steps;
    }

    // The name between the quotes, where \\ and \' stand for a backslash and a quote, and where the path goes on after "']".
    static (string Name, int Next)? QuotedName(string path, int start)
    {
        var name = new StringBuilder();
        for (var at = start; at < path.Length; at++)
        {
            switch (path[at])
            {
                case '\\' when at + 1 < path.Length:
                    name.Append(path[++at]);
                    break;
                case '\'':
                    return at + 1 < path.Length && path[at + 1] == ']' ? (name.ToString(), at + 2) : null;
                default:
                    name.Append(path[at]);
                    break;
            }
        }
        return null;
    }

    static bool IsPlain(string name) => name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(IsNameCharacter);

    static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
