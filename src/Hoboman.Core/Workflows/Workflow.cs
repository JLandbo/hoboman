using Hoboman.Core.Auth;

namespace Hoboman.Core.Workflows;

// The id keeps the runs with the workflow when its folder is renamed, so a new workflow with an old name gets none of them.
public sealed record Workflow
{
    public Guid Id { get; init; }

    public IReadOnlyList<WorkflowValue> Parameters { get; init; } = [];

    public IReadOnlyList<WorkflowValue> Variables { get; init; } = [];

    public IReadOnlyList<WorkflowStep> Steps { get; init; } = [];

    // The auth the steps that inherit use, as a folder's auth is for its requests. Its secrets are kept under the workflow's id.
    public AuthSettings? Auth { get; init; }
}
