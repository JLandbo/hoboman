using System.Text;
using System.Text.Json;

namespace Hoboman.Core.Base64;

// Where each property of a JSON text is, so a checkbox can be put beside it.
public static class JsonOutline
{
    // Null when the text is not JSON.
    public static IReadOnlyList<JsonProperty>? Of(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        var lines = new LineCounter(bytes);
        var containers = new Stack<Container>();
        var properties = new List<JsonProperty>();
        (int Line, string Path, string Place)? named = null;
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString()!;
                        var owner = containers.Peek();
                        named = (lines.At(reader.TokenStartIndex), JsonPath.Member(owner.Path, name), JsonPath.Member(owner.Place, name));
                        break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray:
                        containers.Pop();
                        break;
                    default:
                        var holdsMore = reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray;
                        var (path, place) = (JsonPath.Root, JsonPath.Root);
                        if (named is { } property)
                        {
                            properties.Add(new(property.Line, property.Path, property.Place));
                            (path, place, named) = (property.Path, property.Place, null);
                        }
                        else if (containers.TryPeek(out var list))
                        {
                            (path, place) = (JsonPath.Each(list.Path), JsonPath.Element(list.Place, list.Count++));
                        }
                        if (holdsMore)
                        {
                            containers.Push(new(path, place));
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }
        return properties;
    }

    // Only a list counts its elements, as the values in an object always come after their names.
    sealed class Container(string path, string place)
    {
        public string Path => path;

        public string Place => place;

        public int Count { get; set; }
    }

    // The tokens come in order, so each line break is counted once.
    sealed class LineCounter(byte[] bytes)
    {
        int _line = 1;
        int _counted;

        public int At(long offset)
        {
            _line += bytes.AsSpan(_counted, (int)offset - _counted).Count((byte)'\n');
            _counted = (int)offset;
            return _line;
        }
    }
}
