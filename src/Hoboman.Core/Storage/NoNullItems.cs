using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hoboman.Core.Storage;

// RespectNullableAnnotations does not look inside lists, so a null item in a hand-edited file would get through and break the code that reads it.
// The list is checked after the serializer has read it, so a mistake inside the list is still told with the path and line in the whole file.
public static class NoNullItems
{
    public static void Check(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type.IsGenericType && typeInfo.Type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) && !typeInfo.Type.GetGenericArguments()[0].IsValueType)
        {
            typeInfo.OnDeserialized = list =>
            {
                if (((IEnumerable<object?>)list).Contains(null))
                {
                    throw new JsonException("A list holds an empty item (null).");
                }
            };
        }
    }
}
