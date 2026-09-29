using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.Sending;

public sealed record Echo(string Method, string Target, Dictionary<string, string> Headers, string Body);

public sealed class EchoServer : IAsyncLifetime
{
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
        using var reader = new StreamReader(context.Request.Body);
        var headers = context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        await context.Response.WriteAsJsonAsync(new Echo(context.Request.Method, context.Request.Path + context.Request.QueryString, headers, await reader.ReadToEndAsync()));
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
