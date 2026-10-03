namespace Hoboman.Core.Workflows;

public sealed record StepResult(int Index, StepOutcome Outcome, int? Status = null);
