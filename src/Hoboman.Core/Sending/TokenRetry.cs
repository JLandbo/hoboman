using Hoboman.Core.Auth;

namespace Hoboman.Core.Sending;

// A call that fails for want of a token that can be fetched unasked gets a new one and is made once more, from a tab and from a workflow step alike.
// The calls are awaited without ConfigureAwait(false), so the token is fetched where the caller called from, such as the UI thread.
public static class TokenRetry
{
    public static async Task<ApiResponse> SendAsync(Func<Task<ApiResponse>> send, Func<AuthSource?> auth, Func<AuthSource, Task<bool>> fetchToken)
    {
        try
        {
            var response = await send();
            if (response.StatusCode != 401 || !await FetchedAsync())
            {
                return response;
            }
        }
        catch (Exception exception) when (exception is ExpiredTokenException or MissingSecretException { Kind: SecretKind.OAuthToken })
        {
            if (!await FetchedAsync())
            {
                throw;
            }
        }
        return await send();

        async Task<bool> FetchedAsync() => auth() is { } source && UnaskedTokens.CanFetch(source.Settings) && await fetchToken(source);
    }
}
