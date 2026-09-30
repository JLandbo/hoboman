using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hoboman.Tests.Auth;

// A token endpoint that answers by path, and keeps the last request so a test can see what was sent.
public sealed class TokenServer : IAsyncLifetime
{
    WebApplication? _app;

    public Uri Address { get; private set; } = null!;

    public TokenRequest? Last { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();
        _app.Run(AnswerAsync);
        await _app.StartAsync();
        Address = new Uri(_app.Urls.Single());
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    async Task AnswerAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        Last = new(context.Request.Headers.Authorization.ToString(), form.ToDictionary(field => field.Key, field => field.Value.ToString()));
        if (context.Request.Path == "/redirects")
        {
            context.Response.StatusCode = 307;
            context.Response.Headers.Location = "/token";
            return;
        }
        var (status, body) = context.Request.Path.Value switch
        {
            "/rejects" => (400, """{"error": "invalid_client", "error_description": "Bad secret"}"""),
            "/mac" => (200, """{"access_token": "access", "token_type": "mac"}"""),
            "/no-token" => (200, """{"token_type": "Bearer"}"""),
            "/error-with-200" => (200, """{"error": "bad_verification_code", "error_description": "The code is wrong"}"""),
            "/huge-expiry" => (200, """{"access_token": "access", "token_type": "Bearer", "expires_in": 1e20}"""),
            "/text-expiry" => (200, """{"access_token": "access", "token_type": "Bearer", "expires_in": "3600"}"""),
            _ => (200, """{"access_token": "access", "token_type": "Bearer", "expires_in": 3600, "scope": "read"}"""),
        };
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body);
    }
}

public sealed record TokenRequest(string Authorization, Dictionary<string, string> Form);
