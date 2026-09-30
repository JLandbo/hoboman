namespace Hoboman.Tests;

public sealed class FakeSender(Func<Task<ApiResponse>> send) : IRequestSender
{
    public ApiRequest? Request { get; private set; }

    public AuthSource? Auth { get; private set; }

    public ApiEnvironment? Environment { get; private set; }

    public Task<ApiResponse> SendAsync(ApiRequest request, AuthSource auth, ApiEnvironment? environment, CancellationToken cancellationToken)
    {
        Request = request;
        Auth = auth;
        Environment = environment;
        return send().WaitAsync(cancellationToken);
    }
}
