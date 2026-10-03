using Hoboman.Core.Auth;

namespace Hoboman.Cli;

// Only tokens for client credentials, which need no login, are fetched here, so a browser is never opened.
sealed class NoBrowser : IBrowser
{
    public void Open(Uri address) => throw new InvalidOperationException("The CLI does not open a browser.");
}
