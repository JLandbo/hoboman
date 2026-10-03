using Hoboman.Core.Auth;
using Hoboman.Core.Base64;

namespace Hoboman.Core.Sending;

// Told by the kind of problem and never copied from the exception, as its message can hold values such as a token in an address.
public static class RequestProblem
{
    public static string Of(Exception exception, CancellationToken cancellationToken) => TextOf(KindOf(exception, cancellationToken));

    public static RequestProblemKind KindOf(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => cancellationToken.IsCancellationRequested ? RequestProblemKind.Cancelled : RequestProblemKind.TimedOut,
        MissingSecretException { Kind: SecretKind.OAuthToken } or ExpiredTokenException => RequestProblemKind.MissingOAuthToken,
        MissingSecretException => RequestProblemKind.MissingSecret,
        UriFormatException => RequestProblemKind.InvalidUrl,
        HttpRequestException => RequestProblemKind.NetworkFailed,
        InvalidMethodException or InvalidHeaderException or FormatException or ArgumentException => RequestProblemKind.InvalidInput,
        InvalidBase64RequestBodyException or MissingBase64PathException => RequestProblemKind.BodyNotEncoded,
        IOException or UnauthorizedAccessException => RequestProblemKind.InputUnreadable,
        _ => RequestProblemKind.Failed,
    };

    public static string TextOf(RequestProblemKind kind) => kind switch
    {
        RequestProblemKind.Cancelled => "Request was cancelled.",
        RequestProblemKind.TimedOut => "Request timed out.",
        RequestProblemKind.MissingOAuthToken => "Fetch a new OAuth token in Hoboman before sending this request.",
        RequestProblemKind.MissingSecret => "Required authentication secret is missing.",
        RequestProblemKind.InvalidUrl => "Invalid request URL.",
        RequestProblemKind.NetworkFailed => "Network request failed.",
        RequestProblemKind.InvalidInput => "Invalid request input.",
        RequestProblemKind.BodyNotEncoded => "Request body could not be encoded.",
        RequestProblemKind.InputUnreadable => "Input could not be read.",
        _ => "Request failed.",
    };
}
