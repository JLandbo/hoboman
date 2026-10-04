using System.Text.Json;
using Hoboman.Core.Base64;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Text;

namespace Hoboman.Core.Workflows;

// The values of one run. Each value's {{name}} text is made once when it is set, as making it for every step was slow once a large value was saved.
public sealed class RunValues
{
    const string _header = "header:";
    const string _status = "status";

    readonly Dictionary<string, (JsonElement Value, string Text)> _values = [];

    // As in ADF, a run starts from the defaults, and nothing is kept for the next run.
    public static RunValues Start(Workflow workflow, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var values = new RunValues();
        foreach (var value in workflow.Parameters.Concat(workflow.Variables).Where(value => value.HasDefault))
        {
            values.Set(value.Name, value.Default);
        }
        foreach (var (name, value) in parameters)
        {
            values.Set(name, value);
        }
        return values;
    }

    public IReadOnlyList<KeyValue> Overlay => [.. _values.Select(value => new KeyValue(value.Key, value.Value.Text))];

    public IReadOnlyDictionary<string, JsonElement> Of(IEnumerable<WorkflowValue> declared) =>
        declared.Where(value => _values.ContainsKey(value.Name)).ToDictionary(value => value.Name, value => _values[value.Name].Value);

    // "$" is the whole body, also when it is not JSON, a path is a value in it, "header:Name" the first value of a header and "status" the status code.
    public static bool IsSource(string from) => from == _status || from.StartsWith(_header, StringComparison.Ordinal) && from.Length > _header.Length || JsonPath.CanSelect(from);

    // Text goes in as it is, and any other value as compact JSON, so an object put in a body without quotes is JSON there too.
    public static string TextOf(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : JsonSerializer.Serialize(value, CompactJson.Options);

    // The value a source picks out of an answer, or null when it is not there.
    public static JsonElement? ValueOf(string from, ApiResponse response)
    {
        var body = new Lazy<JsonDocument?>(() => DocumentOf(response.Body));
        try
        {
            return ValueOf(from, response, body);
        }
        finally
        {
            if (body.IsValueCreated)
            {
                body.Value?.Dispose();
            }
        }
    }

    // All values are found before any is set, so a step saves all or nothing.
    public IReadOnlyDictionary<string, JsonElement>? TrySave(IReadOnlyList<WorkflowSave> saves, ApiResponse response, out string? missing)
    {
        missing = null;
        var body = new Lazy<JsonDocument?>(() => DocumentOf(response.Body));
        try
        {
            var saved = new Dictionary<string, JsonElement>();
            foreach (var save in saves)
            {
                if (ValueOf(save.From, response, body) is not { } value)
                {
                    missing = save.From;
                    return null;
                }
                saved[save.Variable] = value;
            }
            foreach (var (name, value) in saved)
            {
                Set(name, value);
            }
            return saved;
        }
        finally
        {
            if (body.IsValueCreated)
            {
                body.Value?.Dispose();
            }
        }
    }

    void Set(string name, JsonElement value) => _values[name] = (value, TextOf(value));

    // A value is copied out of the body, so a small value does not keep a large response alive.
    static JsonElement? ValueOf(string from, ApiResponse response, Lazy<JsonDocument?> body)
    {
        if (from == _status)
        {
            return JsonSerializer.SerializeToElement(response.StatusCode);
        }
        if (from.StartsWith(_header, StringComparison.Ordinal))
        {
            return response.Headers.FirstOrDefault(header => header.Name.Equals(from[_header.Length..], StringComparison.OrdinalIgnoreCase)) is { } found
                ? JsonSerializer.SerializeToElement(found.Value)
                : null;
        }
        if (body.Value is null)
        {
            return from == JsonPath.Root ? JsonSerializer.SerializeToElement(response.Body) : null;
        }
        return JsonPath.TrySelect(body.Value.RootElement, from, out var value) ? value.Clone() : null;
    }

    static JsonDocument? DocumentOf(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
