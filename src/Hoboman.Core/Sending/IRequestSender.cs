using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.Requests;

namespace Hoboman.Core.Sending;

public interface IRequestSender
{
    Task<ApiResponse> SendAsync(ApiRequest request, AuthSource auth, ApiEnvironment? environment, CancellationToken cancellationToken);
}
