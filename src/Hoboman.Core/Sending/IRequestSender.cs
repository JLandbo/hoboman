using Hoboman.Core.Environments;
using Hoboman.Core.Requests;

namespace Hoboman.Core.Sending;

public interface IRequestSender
{
    Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment? environment, CancellationToken cancellationToken);
}
