namespace Hoboman.Core.Workflows;

// The request is its path when the run starts, only for reading.
public sealed record StepStarted(int Index, string Request, string Method, string Address) : WorkflowEvent;
