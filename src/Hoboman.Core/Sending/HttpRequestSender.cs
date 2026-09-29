using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Sending;

public sealed class HttpRequestSender(SecretStore secrets, JsonFile<AppSettings> settings, ILogger<HttpRequestSender> logger) : IRequestSender, IDisposable
{
    readonly HttpClient _client = new(new SocketsHttpHandler { UseCookies = false });
    readonly HttpClient _trustingClient = new(new SocketsHttpHandler { UseCookies = false, SslOptions = { RemoteCertificateValidationCallback = delegate { return true; } } });

    public async Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        try
        {
            using var message = await MessageOfAsync(request, environment ?? new ApiEnvironment("", []), cancellationToken).ConfigureAwait(false);
            var client = (await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).IgnoreCertificateErrors ? _trustingClient : _client;
            logger.LogInformation("Sending {Method} {Url}", message.Method, message.RequestUri);
            var started = Stopwatch.GetTimestamp();
            using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var size = (await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).LongLength;
            logger.LogInformation("{Method} {Url} answered {StatusCode} in {Elapsed} ms with {Size} bytes", message.Method, message.RequestUri, (int)response.StatusCode, elapsed.TotalMilliseconds, size);
            return new(
                (int)response.StatusCode,
                response.ReasonPhrase ?? "",
                elapsed,
                size,
                [.. response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value.Select(value => new KeyValue(header.Key, value)))],
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("{Method} {Url} was cancelled", request.Method, request.Url);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{Method} {Url} failed", request.Method, request.Url);
            throw;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _trustingClient.Dispose();
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
        foreach (var header in request.Headers.Where(header => header.Enabled))
        {
            var (name, value) = (environment.Resolve(header.Name), environment.Resolve(header.Value));
            if (!message.Headers.TryAddWithoutValidation(name, value) && message.Content is not null)
            {
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }
        if (await AuthorizationOfAsync(request, environment, cancellationToken).ConfigureAwait(false) is { } authorization)
        {
            message.Headers.Authorization = authorization;
        }
        return message;
    }

    static Uri UrlOf(ApiRequest request, ApiEnvironment environment)
    {
        var url = environment.Resolve(request.Url);
        var query = string.Join('&', request.Query.Where(parameter => parameter.Enabled)
            .Select(parameter => $"{Uri.EscapeDataString(environment.Resolve(parameter.Name))}={Uri.EscapeDataString(environment.Resolve(parameter.Value))}"));
        return new Uri(query.Length == 0 ? url : $"{url}{(url.Contains('?') ? '&' : '?')}{query}", UriKind.Absolute);
    }

    async Task<AuthenticationHeaderValue?> AuthorizationOfAsync(ApiRequest request, ApiEnvironment environment, CancellationToken cancellationToken) => request.Auth.Kind switch
    {
        AuthKind.Basic => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{environment.Resolve(request.Auth.UserName)}:{environment.Resolve(await secrets.OfAsync(request.Id, cancellationToken).ConfigureAwait(false))}"))),
        AuthKind.Bearer => new("Bearer", environment.Resolve(await secrets.OfAsync(request.Id, cancellationToken).ConfigureAwait(false))),
        _ => null,
    };
}
