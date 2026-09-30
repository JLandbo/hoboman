using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hoboman.Core.Base64;

// Sends the chosen values of a JSON body as Base64, and shows the chosen values of a response decoded.
// A path that leads nowhere in a body is passed over, as the property may just not be in this one.
public static class Base64Json
{
    // Text that is not ASCII stays as it was written, so it reads the same once decoded.
    static readonly JsonSerializerOptions _asWritten = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // A value that is not text is encoded as its JSON. "$", the whole body, goes last and needs no JSON.
    public static string Encode(string body, IEnumerable<string> paths)
    {
        var chosen = paths.ToList();
        var properties = chosen.Where(path => path != JsonPath.Root).ToList();
        if (properties.Count > 0)
        {
            body = EncodeProperties(body, properties);
        }
        return chosen.Contains(JsonPath.Root) ? Base64Text.Encode(body) : body;
    }

    // "$" is the whole body, which is decoded before its properties, so they can be chosen in a response that came as Base64.
    // The shallowest go first, so a property chosen inside a decoded value is decoded too.
    public static Base64Decoded Decode(string body, IEnumerable<string> paths)
    {
        var chosen = StepsOf(paths).OrderBy(steps => steps.Count).ToList();
        var failed = new HashSet<string>(StringComparer.Ordinal);
        if (chosen.Any(steps => steps.Count == 0))
        {
            if (Base64Text.TryDecode(body.Trim(), out var whole))
            {
                body = whole;
            }
            else
            {
                failed.Add(JsonPath.Root);
            }
        }
        if (chosen.All(steps => steps.Count == 0) || !TryParse(body, out var root) || root is null)
        {
            return new(body, failed);
        }
        var changed = false;
        foreach (var steps in chosen.Where(steps => steps.Count > 0))
        {
            foreach (var slot in SlotsOf(root, steps, 0, JsonPath.Root))
            {
                if (slot.Value is JsonValue value && value.TryGetValue<string>(out var text) && Base64Text.TryDecode(text, out var decoded))
                {
                    // Text that reads as JSON is shown as that JSON, such as an object, a number or false, so a value that went out as JSON comes back as the same kind.
                    slot.Set(TryParse(decoded, out var json) ? json : JsonValue.Create(decoded));
                    changed = true;
                }
                else
                {
                    failed.Add(slot.Place);
                }
            }
        }
        return new(changed ? root.ToJsonString(_asWritten) : body, failed);
    }

    // A chosen path must lead to a value, or the body would go without it encoded and nobody would notice. An empty list has nothing to encode and is fine.
    // All are checked before any is encoded, and the deepest go first, so a property chosen inside another chosen one is encoded before the whole is.
    static string EncodeProperties(string json, IEnumerable<string> paths)
    {
        var root = JsonNode.Parse(json);
        var properties = paths.Select(path => JsonPath.StepsOf(path) is { } steps && Leads(root, steps, 0) ? steps : throw new MissingBase64PathException(path))
            .OrderByDescending(steps => steps.Count).ToList();
        var changed = false;
        foreach (var steps in properties)
        {
            foreach (var slot in SlotsOf(root, steps, 0, JsonPath.Root))
            {
                slot.Set(JsonValue.Create(Base64Text.Encode(TextOf(slot.Value))));
                changed = true;
            }
        }
        return changed ? root!.ToJsonString(_asWritten) : json;
    }

    static IEnumerable<IReadOnlyList<string?>> StepsOf(IEnumerable<string> paths) => paths.Select(JsonPath.StepsOf).OfType<IReadOnlyList<string?>>();

    // The values the steps lead to, each with a way to replace it. A list step goes through every element, and the place names the element.
    static IEnumerable<Slot> SlotsOf(JsonNode? node, IReadOnlyList<string?> steps, int at, string place)
    {
        var last = at == steps.Count - 1;
        return (node, steps[at]) switch
        {
            (JsonObject members, { } name) when members.TryGetPropertyValue(name, out var value) =>
                last ? [new(members, name, 0, JsonPath.Member(place, name))] : SlotsOf(value, steps, at + 1, JsonPath.Member(place, name)),
            (JsonArray elements, null) => Enumerable.Range(0, elements.Count).SelectMany(index =>
                last ? [new(elements, null, index, JsonPath.Element(place, index))] : SlotsOf(elements[index], steps, at + 1, JsonPath.Element(place, index))),
            _ => [],
        };
    }

    static bool Leads(JsonNode? node, IReadOnlyList<string?> steps, int at) =>
        at == steps.Count || (node, steps[at]) switch
        {
            (JsonObject members, { } name) => members.TryGetPropertyValue(name, out var value) && Leads(value, steps, at + 1),
            (JsonArray elements, null) => elements.All(element => Leads(element, steps, at + 1)),
            _ => false,
        };

    static string TextOf(JsonNode? value) => value is JsonValue text && text.TryGetValue<string>(out var plain) ? plain : value?.ToJsonString(_asWritten) ?? "null";

    // JSON null is a value too, so it is told apart from text that is not JSON.
    static bool TryParse(string text, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    readonly record struct Slot(JsonNode Parent, string? Name, int Index, string Place)
    {
        public JsonNode? Value => Name is { } name ? Parent[name] : Parent[Index];

        public void Set(JsonNode? value)
        {
            if (Name is { } name)
            {
                Parent[name] = value;
            }
            else
            {
                Parent[Index] = value;
            }
        }
    }
}
