using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;

namespace Hoboman.Core.Sending;

public sealed class HttpRequestSender(SecretStore secrets, JsonFile<AppSettings> settings) : IRequestSender, IDisposable
{
    readonly HttpClient _client = new();
    readonly HttpClient _trustingClient = new(new SocketsHttpHandler { SslOptions = { RemoteCertificateValidationCallback = delegate { return true; } } });

    public async Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        using var message = MessageOf(request, environment ?? new ApiEnvironment("", []));
        var client = settings.Load().IgnoreCertificateErrors ? _trustingClient : _client;
        var started = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(message, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var size = (await response.Content.ReadAsByteArrayAsync(cancellationToken)).LongLength;
        return new(
            (int)response.StatusCode,
            response.ReasonPhrase ?? "",
            elapsed,
            size,
            [.. response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value.Select(value => new KeyValue(header.Key, value)))],
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public void Dispose()
    {
        _client.Dispose();
        _trustingClient.Dispose();
    }

    HttpRequestMessage MessageOf(ApiRequest request, ApiEnvironment environment)
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
        if (AuthorizationOf(request, environment) is { } authorization)
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

    AuthenticationHeaderValue? AuthorizationOf(ApiRequest request, ApiEnvironment environment) => request.Auth.Kind switch
    {
        AuthKind.Basic => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{environment.Resolve(request.Auth.UserName)}:{environment.Resolve(secrets.Of(request.Id))}"))),
        AuthKind.Bearer => new("Bearer", environment.Resolve(secrets.Of(request.Id))),
        _ => null,
    };
}
