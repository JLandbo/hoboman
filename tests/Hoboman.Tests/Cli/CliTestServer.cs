using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hoboman.Tests.Sending;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.Cli;

// Answers by path, and counts the calls, so a test can see that nothing was sent.
public sealed class CliTestServer : IAsyncLifetime
{
    static readonly JsonSerializerOptions _json = new(JsonSerializerOptions.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    WebApplication? _application;
    int _requestCount;

    // Larger than a pipe's buffer, so the CLI has to keep writing while the test reads.
    public static string LargeResponse { get; } = string.Concat(Enumerable.Repeat("Ærø 🚀 \"quoted\"\\\n", 150000));

    public Uri Http { get; private set; } = null!;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        _application = builder.Build();
        _application.Run(RespondAsync);
        await _application.StartAsync(TestContext.Current.CancellationToken);
        Http = new(_application.Urls.Single());
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    async Task RespondAsync(HttpContext context)
    {
        Interlocked.Increment(ref _requestCount);
        switch (context.Request.Path.Value)
        {
            case "/abort":
                context.Abort();
                break;
            case "/large":
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(LargeResponse, context.RequestAborted);
                break;
            case "/token":
                await context.Response.WriteAsJsonAsync(new { access_token = "fetched", token_type = "Bearer", expires_in = 3600 }, context.RequestAborted);
                break;
            default:
                await EchoAsync(context);
                break;
        }
    }

    static async Task EchoAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var headers = context.Request.Headers.ToDictionary(header => header.Key, header => $"{header.Value}", StringComparer.OrdinalIgnoreCase);
        await context.Response.WriteAsJsonAsync(new Echo(context.Request.Method, $"{context.Request.Path}{context.Request.QueryString}", headers, await reader.ReadToEndAsync(context.RequestAborted)), _json, context.RequestAborted);
    }
}
