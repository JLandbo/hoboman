using System.Globalization;
using System.Text;
using System.Text.Json;

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
    // An element's place, as "[0]", only selects a value, so a path with one is not such a path.
    public static IReadOnlyList<string?>? StepsOf(string path) =>
        PartsOf(path) is { } parts && parts.All(part => part.Index is null) ? [.. parts.Select(part => part.Name)] : null;

    // The place a path selects, written as a body's places are, so "$['id']" and "$.id" are the same place. Null when it selects no single place.
    public static string? PlaceOf(string path) => PartsOf(path) is { } parts && !parts.Any(part => part.IsEach)
        ? parts.Aggregate(Root, (place, part) => part.Name is { } name ? Member(place, name) : Element(place, part.Index!.Value))
        : null;

    // Selecting picks one value, so "[*]" cannot be used, while "[0]" can.
    public static bool CanSelect(string path) => PartsOf(path) is { } parts && !parts.Any(part => part.IsEach);

    // A value that is missing is told apart from JSON null, which is a value too.
    public static bool TrySelect(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        if (PartsOf(path) is not { } parts || parts.Any(part => part.IsEach))
        {
            return false;
        }
        var current = root;
        foreach (var part in parts)
        {
            if (!TryStep(current, part, out current))
            {
                return false;
            }
        }
        value = current;
        return true;
    }

    static bool TryStep(JsonElement element, Part part, out JsonElement next)
    {
        next = default;
        if (part.Name is { } name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out next);
        }
        if (element.ValueKind != JsonValueKind.Array || part.Index >= element.GetArrayLength())
        {
            return false;
        }
        next = element[part.Index!.Value];
        return true;
    }

    static List<Part>? PartsOf(string path)
    {
        if (!path.StartsWith(Root, StringComparison.Ordinal))
        {
            return null;
        }
        var parts = new List<Part>();
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
                parts.Add(new(path[(at + 1)..end], null));
                at = end;
            }
            else if (path.AsSpan(at).StartsWith("[*]"))
            {
                parts.Add(new(null, null));
                at += 3;
            }
            else if (path.AsSpan(at).StartsWith("['") && QuotedName(path, at + 2) is (var quoted, var next))
            {
                parts.Add(new(quoted, null));
                at = next;
            }
            else if (path[at] == '[' && IndexOf(path, at + 1) is (var index, var afterIndex))
            {
                parts.Add(new(null, index));
                at = afterIndex;
            }
            else
            {
                return null;
            }
        }
        return parts;
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

    // The digits between the brackets, and where the path goes on after "]".
    static (int Index, int Next)? IndexOf(string path, int start)
    {
        var end = path.IndexOf(']', start);
        return end > start && int.TryParse(path.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? (index, end + 1) : null;
    }

    static bool IsPlain(string name) => name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(IsNameCharacter);

    static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    // A property's name, an element's index, or neither for every element of a list.
    readonly record struct Part(string? Name, int? Index)
    {
        public bool IsEach => Name is null && Index is null;
    }
}
