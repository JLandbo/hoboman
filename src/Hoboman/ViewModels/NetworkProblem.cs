using System.Net.Http;
using Hoboman.Core.Languages;

namespace Hoboman.ViewModels;

// Why a call over the network failed, in words, with .NET's own reason below. Sending and fetching a token share it.
static class NetworkProblem
{
    public static string? Of(Exception exception, Translator translator)
    {
        var reason = exception switch
        {
            UriFormatException => translator.Of("Response.InvalidUrl"),
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => translator.Of("Response.UnknownHost"),
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => translator.Of("Response.NoConnection"),
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => translator.Of("Response.SecureConnection"),
            TaskCanceledException { InnerException: TimeoutException } => translator.Of("Response.Timeout"),
            _ => null,
        };
        return reason is null ? null : $"{reason}{Environment.NewLine}{exception.GetBaseException().Message}";
    }
}
