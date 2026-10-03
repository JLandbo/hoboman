namespace Hoboman.Core.Workflows;

public enum WorkflowProblemKind
{
    MissingId,
    InvalidName,
    DuplicateName,
    UnknownParameter,
    MissingParameter,
    MissingRequest,
    RequestNotFound,
    UnreadableRequests,
    SharedRequestId,
    NotAVariable,
    InvalidSource,
    UnusedWithName,
    UsedBeforeSaved,
    UnknownName,
}
