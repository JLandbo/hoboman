namespace Hoboman.Core.Sending;

public enum RequestProblemKind
{
    Cancelled,
    TimedOut,
    MissingOAuthToken,
    MissingSecret,
    InvalidUrl,
    NetworkFailed,
    InvalidInput,
    BodyNotEncoded,
    InputUnreadable,
    Failed,
}
