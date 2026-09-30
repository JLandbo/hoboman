using Hoboman.Core.Settings;

namespace Hoboman.Core.Sending;

public sealed class HttpClients(SettingsStore settings) : IDisposable
{
    readonly HttpClient _client = ClientOf(trusting: false, redirects: true);
    readonly HttpClient _trustingClient = ClientOf(trusting: true, redirects: true);
    // Token requests carry the client secret and the code, so a redirect must not take them to another address.
    readonly HttpClient _tokenClient = ClientOf(trusting: false, redirects: false);
    readonly HttpClient _trustingTokenClient = ClientOf(trusting: true, redirects: false);

    public async Task<HttpClient> CurrentAsync(CancellationToken cancellationToken) => await TrustsAsync(cancellationToken).ConfigureAwait(false) ? _trustingClient : _client;

    public async Task<HttpClient> TokenClientAsync(CancellationToken cancellationToken) => await TrustsAsync(cancellationToken).ConfigureAwait(false) ? _trustingTokenClient : _tokenClient;

    public void Dispose()
    {
        _client.Dispose();
        _trustingClient.Dispose();
        _tokenClient.Dispose();
        _trustingTokenClient.Dispose();
    }

    async Task<bool> TrustsAsync(CancellationToken cancellationToken) => (await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).IgnoreCertificateErrors;

    static HttpClient ClientOf(bool trusting, bool redirects)
    {
        var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = redirects };
        if (trusting)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = delegate { return true; };
        }
        return new(handler);
    }
}
