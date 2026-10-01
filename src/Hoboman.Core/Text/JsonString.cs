using System.Text.Encodings.Web;
using System.Text.Json;

namespace Hoboman.Core.Text;

// Text written as a JSON string, such as a payload to put into the value of another one, and the text a JSON string holds.
public static class JsonString
{
    // Letters that are not ASCII stay as they are, as in the files and bodies.
    static readonly JsonSerializerOptions _asWritten = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // JSON is made compact first, so it goes in on one line without the spaces of its layout. Any other text goes in as it is.
    public static string Of(string text) => JsonSerializer.Serialize(CompactOf(text) ?? text, _asWritten);

    // Also when it was copied without its quotes, but not plain text, which would only come back as it was.
    public static string? From(string text)
    {
        var trimmed = text.Trim();
        return TextOf(trimmed) ?? (TextOf($"\"{trimmed}\"") is { } unquoted && unquoted != trimmed ? unquoted : null);
    }

    static string? CompactOf(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(json.RootElement, _asWritten);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? TextOf(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
