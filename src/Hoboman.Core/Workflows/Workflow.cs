using Hoboman.Core.Auth;

namespace Hoboman.Core.Workflows;

// Its folder is named by its id, so its name can be anything, and its runs stay with it when it is renamed.
public sealed record Workflow
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public IReadOnlyList<WorkflowValue> Parameters { get; init; } = [];

    public IReadOnlyList<WorkflowValue> Variables { get; init; } = [];

    public IReadOnlyList<WorkflowStep> Steps { get; init; } = [];

    // The auth the steps that inherit use, as a folder's auth is for its requests. Its secrets are kept under the workflow's id.
    public AuthSettings? Auth { get; init; }
}
