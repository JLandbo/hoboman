using Hoboman.Core.Languages;
using Hoboman.Core.Sending;

namespace Hoboman.ViewModels;

// What a problem says in the app, by its kind, as a workflow step and a call from the history tell it.
static class RequestProblemTexts
{
    public static string TextOf(RequestProblemKind kind, Translator translator) => kind switch
    {
        RequestProblemKind.Cancelled => translator.Of("RequestProblem.Cancelled"),
        RequestProblemKind.TimedOut => translator.Of("RequestProblem.TimedOut"),
        RequestProblemKind.MissingOAuthToken => translator.Of("RequestProblem.MissingOAuthToken"),
        RequestProblemKind.MissingSecret => translator.Of("RequestProblem.MissingSecret"),
        RequestProblemKind.InvalidUrl => translator.Of("RequestProblem.InvalidUrl"),
        RequestProblemKind.NetworkFailed => translator.Of("RequestProblem.NetworkFailed"),
        RequestProblemKind.InvalidInput => translator.Of("RequestProblem.InvalidInput"),
        RequestProblemKind.BodyNotEncoded => translator.Of("RequestProblem.BodyNotEncoded"),
        RequestProblemKind.InputUnreadable => translator.Of("RequestProblem.InputUnreadable"),
        _ => translator.Of("RequestProblem.Failed"),
    };
}
