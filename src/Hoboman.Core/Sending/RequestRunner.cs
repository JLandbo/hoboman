using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
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
            await RememberAsync(new(DateTimeOffset.Now, source, name, environment?.Name, AddressOf(request, environment), request, null, exception.Message)).ConfigureAwait(false);
            throw;
        }
        await RememberAsync(new(DateTimeOffset.Now, source, name, environment?.Name, AddressOf(request, environment), request, response, null)).ConfigureAwait(false);
        return response;
    }

    async Task RememberAsync(HistoryEntry entry)
    {
        try
        {
            await history.AddAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not add the call to the history");
        }
    }

    // Leaves out the query and user info, which can hold keys and passwords.
    static string AddressOf(ApiRequest request, ApiEnvironment? environment)
    {
        var url = environment?.Resolve(request.Url) ?? request.Url;
        return Uri.TryCreate(url, UriKind.Absolute, out var address) ? address.GetComponents(UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.Unescaped) : url;
    }
}
