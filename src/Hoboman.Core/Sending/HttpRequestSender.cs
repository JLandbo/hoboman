using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Sending;

public sealed class HttpRequestSender(SecretStore secrets, HttpClients clients, ILogger<HttpRequestSender> logger) : IRequestSender
{
    const string _tokenSymbols = "!#$%&'*+-.^_`|~";

    public async Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        Uri? address = null;
        try
        {
            using var message = await MessageOfAsync(request, environment ?? new ApiEnvironment("", []), cancellationToken).ConfigureAwait(false);
            address = message.RequestUri;
            var client = await clients.CurrentAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Sending {Method} {Url}", message.Method, LoggableOf(address));
            var started = Stopwatch.GetTimestamp();
            using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var size = (await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).LongLength;
            logger.LogInformation("{Method} {Url} answered {StatusCode} in {Elapsed} ms with {Size} bytes", message.Method, LoggableOf(address), (int)response.StatusCode, elapsedMs, size);
            return new(
                (int)response.StatusCode,
                response.ReasonPhrase ?? "",
                elapsedMs,
                size,
                [.. response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value.Select(value => new ResponseHeader(header.Key, value)))],
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("{Method} {Url} was cancelled", request.Method, LoggableOf(address));
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{Method} {Url} failed", request.Method, LoggableOf(address));
            throw;
        }
    }

    async Task<HttpRequestMessage> MessageOfAsync(ApiRequest request, ApiEnvironment environment, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), UrlOf(request, environment))
        {
            Content = request.BodyKind switch
            {
                BodyKind.Json => new StringContent(environment.Resolve(request.Body), Encoding.UTF8, "application/json"),
                BodyKind.Text => new StringContent(environment.Resolve(request.Body), Encoding.UTF8, "text/plain"),
                _ => null,
            },
        };
        AddHeaders(message, request, environment);
        if (await AuthorizationOfAsync(request, environment, cancellationToken).ConfigureAwait(false) is { } authorization)
        {
            message.Headers.Authorization = authorization;
        }
        return message;
    }

    void AddHeaders(HttpRequestMessage message, ApiRequest request, ApiEnvironment environment)
    {
        foreach (var header in request.Headers.Where(header => header.Enabled))
        {
            var (name, value) = (environment.Resolve(header.Name), environment.Resolve(header.Value));
            if (string.IsNullOrWhiteSpace(name) || message.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }
            if (!name.All(symbol => char.IsAsciiLetterOrDigit(symbol) || _tokenSymbols.Contains(symbol)))
            {
                throw new FormatException($"'{name}' is not a valid header name");
            }
            if (message.Content is null)
            {
                logger.LogDebug("Left out the {Header} header because the request has no body", name);
                continue;
            }
            message.Content.Headers.Remove(name);
            message.Content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    static Uri UrlOf(ApiRequest request, ApiEnvironment environment)
    {
        var url = environment.Resolve(request.Url);
        var query = string.Join('&', request.Query.Where(parameter => parameter.Enabled)
            .Select(parameter => $"{Uri.EscapeDataString(environment.Resolve(parameter.Name))}={Uri.EscapeDataString(environment.Resolve(parameter.Value))}"));
        if (query.Length == 0)
        {
            return new Uri(url, UriKind.Absolute);
        }
        var hash = url.IndexOf('#');
        var (address, fragment) = hash < 0 ? (url, "") : (url[..hash], url[hash..]);
        return new Uri($"{address}{(address.Contains('?') ? '&' : '?')}{query}{fragment}", UriKind.Absolute);
    }

    async Task<AuthenticationHeaderValue?> AuthorizationOfAsync(ApiRequest request, ApiEnvironment environment, CancellationToken cancellationToken) => request.Auth.Kind switch
    {
        AuthKind.Basic => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{environment.Resolve(request.Auth.UserName)}:{environment.Resolve(await SecretOfAsync(request, SecretKind.Password, cancellationToken).ConfigureAwait(false))}"))),
        AuthKind.Bearer => new("Bearer", environment.Resolve(await SecretOfAsync(request, SecretKind.Token, cancellationToken).ConfigureAwait(false))),
        _ => null,
    };

    async Task<string> SecretOfAsync(ApiRequest request, SecretKind kind, CancellationToken cancellationToken) =>
        await secrets.OfAsync(request.Id, kind, cancellationToken).ConfigureAwait(false) ?? throw new MissingSecretException(kind);

    static string LoggableOf(Uri? address) => address is null ? "(no address yet)" : SafeAddress.Of(address);
}
