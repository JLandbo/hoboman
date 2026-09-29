using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoboman.Core.Storage;

// RespectNullableAnnotations does not look inside lists, so a null item in a hand-edited file would get through and break the code that reads it.
public sealed class NoNullItems : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) && !typeToConvert.GetGenericArguments()[0].IsValueType;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(ListConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;

    sealed class ListConverter<T> : JsonConverter<IReadOnlyList<T>> where T : class
    {
        public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var items = JsonSerializer.Deserialize<List<T>>(ref reader, options)!;
            return items.Any(item => item is null) ? throw new JsonException("A list holds an empty item (null).") : items;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) => JsonSerializer.Serialize<IEnumerable<T>>(writer, value, options);
    }
}
