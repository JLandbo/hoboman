namespace Hoboman.Tests;

public sealed class FakeSender(Func<Task<ApiResponse>> send) : IRequestSender
{
    public Task<ApiResponse> SendAsync(ApiRequest request, ApiEnvironment? environment, CancellationToken cancellationToken) => send().WaitAsync(cancellationToken);
}
