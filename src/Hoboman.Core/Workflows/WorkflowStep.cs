using System.Text.Json.Serialization;
using Hoboman.Core.Scripts;

namespace Hoboman.Core.Workflows;

// A step sends its own request, runs a script in the workflow's folder or waits a number of milliseconds. A step without a URL is told of by the check instead of failing the whole file.
public sealed record WorkflowStep
{
    public string? Name { get; init; }

    public WorkflowRequest? Request { get; init; }

    public string? Script { get; init; }

    // What a script returns, which is JSON when it is not given.
    public ScriptOutput? Output { get; init; }

    public int? DelayMilliseconds { get; init; }

    public WorkflowRetry? Retry { get; init; }

    public IReadOnlyList<WorkflowSave> Saves { get; init; } = [];

    // What the step does follows from what it holds. One that holds more than one thing is told of by the check, and runs its script as before.
    [JsonIgnore]
    public StepKind Kind => Script is not null ? StepKind.Script : DelayMilliseconds is not null ? StepKind.Delay : StepKind.Request;

    [JsonIgnore]
    public bool IsMixed => (Request is null ? 0 : 1) + (Script is null ? 0 : 1) + (DelayMilliseconds is null ? 0 : 1) > 1;
}
