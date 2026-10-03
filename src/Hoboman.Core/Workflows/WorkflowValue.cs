using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoboman.Core.Workflows;

// A missing default is left undefined, so JSON null can be a default too.
public sealed record WorkflowValue(string Name)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Default { get; init; }

    [JsonIgnore]
    public bool HasDefault => Default.ValueKind != JsonValueKind.Undefined;
}
