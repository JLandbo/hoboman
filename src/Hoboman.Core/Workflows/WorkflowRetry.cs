using System.Text.Json.Serialization;

namespace Hoboman.Core.Workflows;

// A request step is sent again until its answer is ready: a 2xx with everything it saves, and, when Until is given, the value there equal to Value, whatever its case.
// When StopIf is given and the value there equals StopEquals, the step fails at once, as when what was waited for failed.
public sealed record WorkflowRetry
{
    public string? Until { get; init; }

    [JsonPropertyName("equals")]
    public string? Value { get; init; }

    public string? StopIf { get; init; }

    public string? StopEquals { get; init; }

    public int Times { get; init; } = 30;

    public int WaitSeconds { get; init; } = 5;
}
