namespace Hoboman.Tests;

public sealed class FakeSender(Func<Task<ApiResponse>> send) : IRequestSender
{
    public AuthSource? Auth { get; private set; }

    public ApiEnvironment? Environment { get; private set; }

    public Task<ApiResponse> SendAsync(ApiRequest request, AuthSource auth, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        Auth = auth;
        Environment = environment;
        return send().WaitAsync(cancellationToken);
    }
}
