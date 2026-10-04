using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Sending;

public sealed class RequestRunner(IRequestSender sender, RequestLibrary library, HistoryStore history, ILogger<RequestRunner> logger)
{
    // The calls are awaited without ConfigureAwait(false), so the token is fetched where the caller called from, such as the UI thread.
    // A call is one entry in the history with how it ended, so a token fetched on the way leaves no failed call that was never sent.
    public async Task<ApiResponse> RunAsync(ApiRequest request, string? name, ApiEnvironment? environment, HistorySource source, Func<AuthSource, Task<bool>> fetchToken, CancellationToken cancellationToken)
    {
        AuthSource? auth = null;
        ApiResponse response;
        try
        {
            response = await TokenRetry.SendAsync(SendOnceAsync, () => auth, fetchToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            await RememberAsync(EntryOf(null, exception.Message) with { Problem = RequestProblem.KindOf(exception, cancellationToken) }).ConfigureAwait(false);
            throw;
        }
        await RememberAsync(EntryOf(response, null)).ConfigureAwait(false);
        return response;

        async Task<ApiResponse> SendOnceAsync()
        {
            auth = await library.AuthOfAsync(name, request, cancellationToken).ConfigureAwait(false);
            return await sender.SendAsync(request, auth, environment, cancellationToken).ConfigureAwait(false);
        }

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

    public static string AddressOf(ApiRequest request, ApiEnvironment? environment)
    {
        var url = environment?.Resolve(request.Url) ?? request.Url;
        return Uri.TryCreate(url, UriKind.Absolute, out var address) ? SafeAddress.Of(address) : url.Split('?', '#')[0];
    }
}
