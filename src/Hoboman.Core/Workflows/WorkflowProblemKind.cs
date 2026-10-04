namespace Hoboman.Core.Workflows;

public enum WorkflowProblemKind
{
    MissingId,
    InvalidName,
    DuplicateName,
    UnknownParameter,
    MissingParameter,
    MissingUrl,
    NotAVariable,
    InvalidSource,
    UsedBeforeSaved,
    UnknownName,
    ScriptNotFound,
    InvalidScript,
    MixedStep,
    InvalidDelay,
    InvalidRetry,
}
