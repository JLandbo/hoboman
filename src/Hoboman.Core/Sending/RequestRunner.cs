using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Sending;

public sealed class RequestRunner(IRequestSender sender, RequestLibrary library, HistoryStore history, ILogger<RequestRunner> logger)
{
    // A call that fails for want of a token that can be fetched unasked gets a new one and is made once more.
    // The calls are awaited without ConfigureAwait(false), so the token is fetched where the caller called from, such as the UI thread.
    public async Task<ApiResponse> RunAsync(ApiRequest request, string? name, ApiEnvironment? environment, HistorySource source, Func<AuthSource, Task<bool>> fetchToken, CancellationToken cancellationToken)
    {
        AuthSource? auth = null;
        try
        {
            var response = await RunOnceAsync();
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
        return await RunOnceAsync();

        async Task<ApiResponse> RunOnceAsync()
        {
            ApiResponse response;
            try
            {
                auth = await library.AuthOfAsync(name, request, cancellationToken).ConfigureAwait(false);
                response = await sender.SendAsync(request, auth, environment, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                await RememberAsync(EntryOf(null, exception.Message)).ConfigureAwait(false);
                throw;
            }
            await RememberAsync(EntryOf(response, null)).ConfigureAwait(false);
            return response;
        }

        async Task<bool> FetchedAsync() => auth is not null && UnaskedTokens.CanFetch(auth.Settings) && await fetchToken(auth);

        HistoryEntry EntryOf(ApiResponse? answer, string? error) =>
            new(DateTimeOffset.Now, source, AddressOf(request, environment), request, name, environment?.Name, answer, error);
    }

    async Task RememberAsync(HistoryEntry entry)
    {
        try
        {
            await history.AddAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not add the call to the history");
        }
    }

    static string AddressOf(ApiRequest request, ApiEnvironment? environment)
    {
        var url = environment?.Resolve(request.Url) ?? request.Url;
        return Uri.TryCreate(url, UriKind.Absolute, out var address) ? SafeAddress.Of(address) : url;
    }
}
