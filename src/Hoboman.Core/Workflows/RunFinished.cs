using System.Text.Json;

namespace Hoboman.Core.Workflows;

public sealed record RunFinished(RunOutcome Outcome, long ElapsedMs, IReadOnlyList<StepResult> Steps, IReadOnlyDictionary<string, JsonElement> Variables) : WorkflowEvent;
