namespace Hoboman.Core.Workflows;

// The detail holds names, paths and ids only, never values, as values can be secrets.
public sealed record WorkflowProblem(WorkflowProblemKind Kind, int? Step, string Detail);
