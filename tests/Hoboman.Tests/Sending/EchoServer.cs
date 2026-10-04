using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.Sending;

public sealed class EchoServer : IAsyncLifetime
{
    // Letters beyond ASCII are sent back as they are, so the body can be longer in bytes than in characters.
    static readonly JsonSerializerOptions _json = new(JsonSerializerOptions.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Sent from /file, and not text, as a PDF is not.
    public static readonly byte[] File = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF, 0xFE, 0x80];

    readonly X509Certificate2 _certificate = SelfSignedCertificate();
    WebApplication? _app;

    public Uri Http { get; private set; } = null!;

    public Uri Https { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0);
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate));
        });
        _app = builder.Build();
        _app.Run(EchoAsync);
        await _app.StartAsync();
        var addresses = _app.Urls.Select(url => new Uri(url));
        Http = addresses.First(address => address.Scheme == Uri.UriSchemeHttp);
        Https = addresses.First(address => address.Scheme == Uri.UriSchemeHttps);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        _certificate.Dispose();
    }

    static async Task EchoAsync(HttpContext context)
    {
        if (context.Request.Path == "/slow")
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }
        if (context.Request.Path == "/file")
        {
            context.Response.ContentType = "application/pdf";
            await context.Response.Body.WriteAsync(File, context.RequestAborted);
            return;
        }
        using var reader = new StreamReader(context.Request.Body);
        context.Response.Cookies.Append("session", "1");
        var headers = context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        await context.Response.WriteAsJsonAsync(new Echo(context.Request.Method, context.Request.Path + context.Request.QueryString, headers, await reader.ReadToEndAsync()), _json);
    }

    static X509Certificate2 SelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        // Windows cannot serve TLS with the in-memory key, so the certificate is round-tripped through PFX.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
