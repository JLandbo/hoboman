using System.Text;
using Hoboman.Core.Auth;
using Hoboman.Core.Requests;

namespace Hoboman.Cli;

static class RequestInput
{
    // A direct request belongs to no folder, so its only auth is what its headers carry.
    public static async Task<ApiRequest> CreateAsync(SendInput input, CancellationToken cancellationToken) =>
        await CreateAsync(input.Target[0], input.Target[1], input.Headers, input.JsonBody, input.TextBody, cancellationToken) with { Auth = AuthSettings.None };

    // A saved request inherits the auth of its folder, as one made in the app does.
    public static async Task<ApiRequest> CreateAsync(string method, string url, IReadOnlyList<string> headers, string? jsonBody, string? textBody, CancellationToken cancellationToken) => ApiRequest.New() with
    {
        Method = method,
        Url = url,
        Headers = [.. headers.Select(HeaderOf)],
        BodyKind = jsonBody is not null ? BodyKind.Json : textBody is not null ? BodyKind.Text : BodyKind.None,
        Body = (jsonBody ?? textBody) is { } body ? await BodyOfAsync(body, cancellationToken) : "",
    };

    // Split at the first colon, as a value such as a time can hold more, and a line break would start a header of its own.
    static KeyValue HeaderOf(string header)
    {
        var separator = header.IndexOf(':');
        if (separator < 0 || header.Contains('\r') || header.Contains('\n') || header[..separator].Trim().Length == 0)
        {
            throw new FormatException();
        }
        return new(header[..separator].Trim(), header[(separator + 1)..].Trim());
    }

    // @file reads the body from a file, and @@ starts a body that begins with @.
    static Task<string> BodyOfAsync(string body, CancellationToken cancellationToken) => body switch
    {
        ['@', '@', ..] => Task.FromResult(body[1..]),
        ['@', ..] => File.ReadAllTextAsync(body[1..], Encoding.UTF8, cancellationToken),
        _ => Task.FromResult(body),
    };
}
