using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace Hoboman.Core.Auth;

// Catches the browser when the login sends it back. It listens on the loopback address only, so nothing on the network can reach it. RFC 8252
sealed class LoopbackCallback : IDisposable
{
    const int _maxHeadLength = 16 * 1024;
    static readonly TimeSpan _pageTimeout = TimeSpan.FromSeconds(5);
    readonly TcpListener _listener;

    LoopbackCallback(TcpListener listener)
    {
        _listener = listener;
        // Without a trailing slash, like the addresses providers usually have registered.
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
    }

    public string RedirectUri { get; }

    // It is started before the browser opens, so the redirect can never arrive before anyone listens.
    public static LoopbackCallback Start(int? port)
    {
        if (port is < 1 or > IPEndPoint.MaxPort)
        {
            throw new OAuthException(OAuthProblem.PortUnavailable, port.ToString());
        }
        var listener = new TcpListener(IPAddress.Loopback, port ?? 0);
        try
        {
            listener.Start();
        }
        // Windows also reserves ranges of ports, which then cannot be listened on.
        catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            listener.Dispose();
            throw new OAuthException(OAuthProblem.PortUnavailable, port?.ToString());
        }
        return new(listener);
    }

    // Browsers open connections before they need them and ask for an icon, so each connection is answered on its own.
    // Only the redirect with this login's state ends the wait, so a stray request cannot end the login.
    public async Task<NameValueCollection> WaitAsync(string state, string page, CancellationToken cancellationToken)
    {
        var redirect = new TaskCompletionSource<NameValueCollection>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            while (true)
            {
                _ = AnswerAsync(await _listener.AcceptTcpClientAsync(done.Token).ConfigureAwait(false), state, page, redirect, done);
            }
        }
        catch (OperationCanceledException) when (done.IsCancellationRequested)
        {
        }
        finally
        {
            // Connections the browser left open are waiting on this, whatever ended the wait.
            await done.CancelAsync().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return await redirect.Task.ConfigureAwait(false);
    }

    public void Dispose() => _listener.Dispose();

    static async Task AnswerAsync(TcpClient client, string state, string page, TaskCompletionSource<NameValueCollection> redirect, CancellationTokenSource done)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var target = await TargetOfAsync(stream, done.Token).ConfigureAwait(false);
                var answer = target is "/" || target?.StartsWith("/?", StringComparison.Ordinal) == true ? HttpUtility.ParseQueryString(target.Length > 1 ? target[2..] : "") : null;
                if (answer?["state"] != state)
                {
                    await WriteAsync(stream, answer is null ? "404 Not Found" : "400 Bad Request", "", done.Token).ConfigureAwait(false);
                    return;
                }
                // The answer is kept before the page is written, so a browser that is gone by then cannot lose the code.
                redirect.TrySetResult(answer!);
                await done.CancelAsync().ConfigureAwait(false);
                using var writing = new CancellationTokenSource(_pageTimeout);
                await WriteAsync(stream, "200 OK", page, writing.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    // The whole head is read before answering, because closing with unread data would reset the connection before the browser shows the page.
    static async Task<string?> TargetOfAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var head = new byte[_maxHeadLength];
        var length = 0;
        while (length < head.Length)
        {
            var read = await stream.ReadAsync(head.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }
            length += read;
            var text = Encoding.ASCII.GetString(head, 0, length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text[..text.IndexOf("\r\n", StringComparison.Ordinal)].Split(' ') is ["GET", var target, _] ? target : null;
            }
        }
        return null;
    }

    static async Task WriteAsync(NetworkStream stream, string status, string text, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(text.Length == 0 ? "" : $"<!doctype html><meta charset=\"utf-8\"><title>Hoboman</title><p style=\"font-family: sans-serif\">{WebUtility.HtmlEncode(text)}</p>");
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }
}
