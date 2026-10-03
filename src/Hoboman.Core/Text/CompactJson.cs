using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoboman.Core.Text;

// JSON written on one line for programs to read, where letters that are not ASCII stay as they are, as in the files and bodies.
public static class CompactJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerOptions.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
