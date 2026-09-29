using Hoboman.Core.Settings;
using Hoboman.Core.Storage;

namespace Hoboman.Core.Sending;

public sealed class HttpClients(JsonFile<AppSettings> settings) : IDisposable
{
    readonly HttpClient _client = new(new SocketsHttpHandler { UseCookies = false });
    readonly HttpClient _trustingClient = new(new SocketsHttpHandler { UseCookies = false, SslOptions = { RemoteCertificateValidationCallback = delegate { return true; } } });

    public async Task<HttpClient> CurrentAsync(CancellationToken cancellationToken) =>
        (await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).IgnoreCertificateErrors ? _trustingClient : _client;

    public void Dispose()
    {
        _client.Dispose();
        _trustingClient.Dispose();
    }
}
