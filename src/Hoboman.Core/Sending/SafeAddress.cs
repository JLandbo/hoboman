namespace Hoboman.Core.Sending;

// The query and user info can hold keys and passwords, so they are left out wherever an address is shown or logged.
public static class SafeAddress
{
    public static string Of(Uri address) => address.GetComponents(UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.Unescaped);
}
