namespace Hoboman.Core.Auth;

public static class CallbackAddress
{
    // Only the callback itself may end the login, so a look-alike on another host or a longer path goes on. The query and fragment carry the answer and are left out.
    public static bool Matches(Uri address, Uri callback) =>
        string.Equals(address.Scheme, callback.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(address.Host, callback.Host, StringComparison.OrdinalIgnoreCase)
        && address.Port == callback.Port
        && string.Equals(address.AbsolutePath, callback.AbsolutePath, StringComparison.Ordinal);
}
