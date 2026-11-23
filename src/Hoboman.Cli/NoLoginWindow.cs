using Hoboman.Core.Auth;

namespace Hoboman.Cli;

// Only tokens for client credentials, which need no login, are fetched here, so a login window is never opened.
sealed class NoLoginWindow : ILoginWindow
{
    public Task<Uri?> GetRedirectAsync(Uri login, Uri callback, CancellationToken cancellationToken) => throw new InvalidOperationException("The CLI does not open a login window.");
}
