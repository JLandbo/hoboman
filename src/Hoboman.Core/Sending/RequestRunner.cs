using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Hoboman.Core.Sending;

public sealed class RequestRunner(IRequestSender sender, HistoryStore history, ILogger<RequestRunner> logger)
{
    public async Task<ApiResponse> RunAsync(ApiRequest request, string? name, ApiEnvironment? environment, HistorySource source, CancellationToken cancellationToken)
    {
        ApiResponse response;
        try
        {
            response = await sender.SendAsync(request, environment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            await RememberAsync(EntryOf(null, exception.Message)).ConfigureAwait(false);
            throw;
        }
        await RememberAsync(EntryOf(response, null)).ConfigureAwait(false);
        return response;

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
