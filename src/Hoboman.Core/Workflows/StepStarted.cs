namespace Hoboman.Core.Workflows;

// The name is the step's own name, or its script or method and URL when it has none.
public sealed record StepStarted(int Index, string Name, string Method, string Address) : WorkflowEvent;
