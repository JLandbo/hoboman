using Hoboman.Core.Requests;

namespace Hoboman.Core.Workflows;

// The request is kept by its id, so renaming or moving it does not break the step. A step without one is told of by the check instead of failing the whole file.
public sealed record WorkflowStep
{
    public Guid Request { get; init; }

    public IReadOnlyList<KeyValue> With { get; init; } = [];

    public IReadOnlyList<WorkflowSave> Saves { get; init; } = [];
}
