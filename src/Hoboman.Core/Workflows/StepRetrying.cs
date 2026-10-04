namespace Hoboman.Core.Workflows;

// Without the body, as a large answer would otherwise fill the log once for every attempt.
public sealed record StepRetrying(int Index, int Attempt, int? Status = null, string? Value = null, string? Error = null) : WorkflowEvent;
