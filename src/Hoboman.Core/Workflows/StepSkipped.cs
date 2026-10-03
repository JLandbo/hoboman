namespace Hoboman.Core.Workflows;

public sealed record StepSkipped(int Index) : WorkflowEvent;
