using System.Buffers.Text;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hoboman.Core.Environments;
using Hoboman.Core.Languages;
using Hoboman.Core.Sending;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Auth;

public sealed class OAuthClient(HttpClients clients, IBrowser browser, Translator translator, TimeProvider clock, ILogger<OAuthClient> logger) : IOAuthClient
{
    static readonly TimeSpan _loginTimeout = TimeSpan.FromMinutes(5);
    static readonly TimeSpan _longestLifetime = TimeSpan.FromDays(3650);

    public async Task<OAuthToken> GetTokenAsync(OAuthSettings settings, string clientSecret, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        // The fields can use the chosen environment's variables, like the rest of the request.
        var variables = environment ?? ApiEnvironment.None;
        settings = settings with
        {
            AuthorizeUrl = variables.Resolve(settings.AuthorizeUrl),
            TokenUrl = variables.Resolve(settings.TokenUrl),
            ClientId = variables.Resolve(settings.ClientId),
            Scope = variables.Resolve(settings.Scope),
        };
        clientSecret = variables.Resolve(clientSecret);
        var tokenUrl = EndpointOf(settings.TokenUrl, OAuthProblem.MissingTokenUrl);
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new OAuthException(OAuthProblem.MissingClientId);
        }
        var form = settings.Grant switch
        {
            OAuthGrant.ClientCredentials => ClientCredentialsForm(settings, clientSecret),
            OAuthGrant.AuthorizationCode => await AuthorizationCodeFormAsync(settings, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Grant, "Unknown grant"),
        };
        return await RequestTokenAsync(tokenUrl, settings, clientSecret, form, cancellationToken).ConfigureAwait(false);
    }

    static List<KeyValuePair<string, string>> ClientCredentialsForm(OAuthSettings settings, string clientSecret)
    {
        // Only a client that can keep a secret may use this grant.
        if (clientSecret.Length == 0)
        {
            throw new OAuthException(OAuthProblem.MissingClientSecret);
        }
        return WithScope([new("grant_type", "client_credentials")], settings);
    }

    // The verifier ties the code to this login, and the state ties the redirect to it, so a code caught by someone else is useless. RFC 7636, RFC 9700
    async Task<List<KeyValuePair<string, string>>> AuthorizationCodeFormAsync(OAuthSettings settings, CancellationToken cancellationToken)
    {
        var authorizeUrl = EndpointOf(settings.AuthorizeUrl, OAuthProblem.MissingAuthorizeUrl);
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        using var callback = LoopbackCallback.Start(settings.RedirectPort);
        var login = WithScope(
            [
                new("response_type", "code"),
                new("client_id", settings.ClientId),
                new("redirect_uri", callback.RedirectUri),
                new("code_challenge", Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))),
                new("code_challenge_method", "S256"),
                new("state", state),
            ],
            settings);
        using var timeout = new CancellationTokenSource(_loginTimeout, clock);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        logger.LogInformation("Opening the browser to log in at {Url}", SafeAddress.Of(authorizeUrl));
        browser.Open(new Uri($"{authorizeUrl.AbsoluteUri}{(authorizeUrl.Query.Length > 0 ? '&' : '?')}{string.Join('&', login.Select(field => $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"))}"));
        NameValueCollection answer;
        try
        {
            answer = await callback.WaitAsync(state, translator.Of("OAuth.Page"), waiting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException(OAuthProblem.TimedOut, _loginTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture));
        }
        // Only a redirect with this login's state gets here, so the error is the provider's own.
        if (answer["error"] is { } error)
        {
            throw new OAuthException(OAuthProblem.Denied, answer["error_description"] is { Length: > 0 } description ? $"{error}: {description}" : error);
        }
        if (answer["code"] is not { Length: > 0 } code)
        {
            throw new OAuthException(OAuthProblem.InvalidResponse);
        }
        logger.LogInformation("The login answered with a code");
        return [new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", callback.RedirectUri), new("code_verifier", verifier)];
    }

    async Task<OAuthToken> RequestTokenAsync(Uri tokenUrl, OAuthSettings settings, string clientSecret, List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        // A client without a secret only says who it is.
        if (clientSecret.Length == 0 || settings.ClientAuthentication == OAuthClientAuthentication.RequestBody)
        {
            form.Add(new("client_id", settings.ClientId));
            if (clientSecret.Length > 0)
            {
                form.Add(new("client_secret", clientSecret));
            }
        }
        else
        {
            // The standard form-encodes the id and the secret before they are joined, so a colon in either cannot be misread.
            message.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WebUtility.UrlEncode(settings.ClientId)}:{WebUtility.UrlEncode(clientSecret)}")));
        }
        message.Content = new FormUrlEncodedContent(form);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var client = await clients.TokenClientAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Asking {Url} for an OAuth token with the {Grant} grant", SafeAddress.Of(tokenUrl), settings.Grant);
        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        // Some providers, such as GitHub, answer an error with 200.
        if (!response.IsSuccessStatusCode || ErrorOf(body) is not null)
        {
            throw new OAuthException(OAuthProblem.Rejected, ErrorOf(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }
        var token = TokenOf(body);
        logger.LogInformation("Got an OAuth token that expires at {ExpiresAt}", token.ExpiresAt);
        return token;
    }

    OAuthToken TokenOf(string body)
    {
        using var json = ObjectOf(body) ?? throw new OAuthException(OAuthProblem.InvalidResponse);
        var answer = json.RootElement;
        if (TextOf(answer, "access_token") is not { Length: > 0 } accessToken || TextOf(answer, "token_type") is not { } tokenType)
        {
            throw new OAuthException(OAuthProblem.InvalidResponse);
        }
        // Other token types, such as DPoP, need more than a header.
        if (!tokenType.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthException(OAuthProblem.UnsupportedTokenType, tokenType);
        }
        return new(accessToken, tokenType, SecondsOf(answer, "expires_in") is { } seconds ? clock.GetUtcNow().AddSeconds(seconds) : null, TextOf(answer, "scope"));
    }

    // Codes and tokens pass through these addresses, so they must be encrypted unless they stay on this computer.
    static Uri EndpointOf(string url, OAuthProblem missing)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new OAuthException(missing);
        }
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address) || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            throw new OAuthException(OAuthProblem.InvalidAddress, url);
        }
        if (address.Scheme == Uri.UriSchemeHttp && !address.IsLoopback)
        {
            throw new OAuthException(OAuthProblem.InsecureAddress, url);
        }
        return address;
    }

    static List<KeyValuePair<string, string>> WithScope(List<KeyValuePair<string, string>> form, OAuthSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Scope))
        {
            form.Add(new("scope", settings.Scope.Trim()));
        }
        return form;
    }

    static string? ErrorOf(string body)
    {
        using var json = ObjectOf(body);
        if (json is null || TextOf(json.RootElement, "error") is not { } error)
        {
            return null;
        }
        return TextOf(json.RootElement, "error_description") is { Length: > 0 } description ? $"{error}: {description}" : error;
    }

    static JsonDocument? ObjectOf(string body)
    {
        try
        {
            var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                return json;
            }
            json.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? TextOf(JsonElement answer, string name) => answer.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Some providers send the lifetime as text. One beyond reason, like infinity, is left out instead of breaking an otherwise good token.
    static double? SecondsOf(JsonElement answer, string name)
    {
        double? seconds = answer.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) => text,
            _ => null,
        } : null;
        return seconds is { } lifetime && double.IsFinite(lifetime) && lifetime <= _longestLifetime.TotalSeconds ? lifetime : null;
    }
}
