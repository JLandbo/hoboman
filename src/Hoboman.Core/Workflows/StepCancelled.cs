namespace Hoboman.Core.Workflows;

public sealed record StepCancelled(int Index) : WorkflowEvent;
