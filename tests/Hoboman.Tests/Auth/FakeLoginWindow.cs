namespace Hoboman.Tests.Auth;

public sealed class FakeLoginWindow(Func<Uri, CancellationToken, Uri?> redirectOf) : ILoginWindow
{
    public (Uri Login, Uri Callback)? Opened { get; private set; }

    public Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken)
    {
        Opened = (login, callback);
        return Task.FromResult(redirectOf(login, cancellationToken));
    }
}
