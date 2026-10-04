namespace Hoboman.Core.Workflows;

// A step sends its own request or runs a script in the workflow's folder. A step without a URL is told of by the check instead of failing the whole file.
public sealed record WorkflowStep
{
    public string? Name { get; init; }

    public WorkflowRequest? Request { get; init; }

    public string? Script { get; init; }

    public IReadOnlyList<WorkflowSave> Saves { get; init; } = [];
}
