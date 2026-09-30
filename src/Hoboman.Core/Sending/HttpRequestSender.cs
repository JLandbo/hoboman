using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Sending;

public sealed class HttpRequestSender(SecretStore secrets, HttpClients clients, TimeProvider clock, ILogger<HttpRequestSender> logger) : IRequestSender
{
    const string _tokenSymbols = "!#$%&'*+-.^_`|~";

    public async Task<ApiResponse> SendAsync(ApiRequest request, AuthSource auth, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        Uri? address = null;
        try
        {
            using var message = await MessageOfAsync(request, auth, environment ?? ApiEnvironment.None, cancellationToken).ConfigureAwait(false);
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

    async Task<HttpRequestMessage> MessageOfAsync(ApiRequest request, AuthSource auth, ApiEnvironment environment, CancellationToken cancellationToken)
    {
        if (!IsToken(request.Method))
        {
            throw new InvalidMethodException(request.Method);
        }
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
        if (await AuthorizationOfAsync(auth, environment, cancellationToken).ConfigureAwait(false) is { } authorization)
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
            if (!IsToken(name))
            {
                throw new InvalidHeaderException(name);
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

    async Task<AuthenticationHeaderValue?> AuthorizationOfAsync(AuthSource auth, ApiEnvironment environment, CancellationToken cancellationToken) => auth.Settings.Kind switch
    {
        AuthKind.Basic => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{environment.Resolve(auth.Settings.UserName)}:{environment.Resolve(await SecretOfAsync(auth, SecretKind.Password, cancellationToken).ConfigureAwait(false))}"))),
        AuthKind.Bearer => new("Bearer", environment.Resolve(await SecretOfAsync(auth, SecretKind.Token, cancellationToken).ConfigureAwait(false))),
        AuthKind.OAuth2 => new("Bearer", (await OAuthTokenOfAsync(auth, environment, cancellationToken).ConfigureAwait(false)).AccessToken),
        _ => null,
    };

    // Each environment has its own token, and a missing or expired one stops the request, so it is never sent without the auth it was set up with.
    async Task<OAuthToken> OAuthTokenOfAsync(AuthSource auth, ApiEnvironment environment, CancellationToken cancellationToken)
    {
        var saved = await secrets.OfAsync(auth.SecretsId, SecretKind.OAuthToken, environment.Name, cancellationToken).ConfigureAwait(false);
        var token = (saved is null ? null : OAuthToken.FromJson(saved)) ?? throw new MissingSecretException(SecretKind.OAuthToken);
        return token.HasExpired(clock.GetUtcNow()) ? throw new ExpiredTokenException(token.ExpiresAt!.Value) : token;
    }

    async Task<string> SecretOfAsync(AuthSource auth, SecretKind kind, CancellationToken cancellationToken) =>
        await secrets.OfAsync(auth.SecretsId, kind, cancellationToken).ConfigureAwait(false) ?? throw new MissingSecretException(kind);

    static string LoggableOf(Uri? address) => address is null ? "(no address yet)" : SafeAddress.Of(address);

    static bool IsToken(string text) => text.Length > 0 && text.All(symbol => char.IsAsciiLetterOrDigit(symbol) || _tokenSymbols.Contains(symbol));
}
