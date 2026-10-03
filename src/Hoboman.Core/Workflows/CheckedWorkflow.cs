using System.Text.Json;

namespace Hoboman.Core.Workflows;

// The requests are read once, so a run uses them as they were when it was checked.
public sealed record CheckedWorkflow(string Name, Workflow Workflow, IReadOnlyDictionary<string, JsonElement> Parameters, IReadOnlyList<CheckedStep> Steps, IReadOnlyList<WorkflowProblem> Problems);
