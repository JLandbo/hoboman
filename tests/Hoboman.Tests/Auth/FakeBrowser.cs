namespace Hoboman.Tests.Auth;

public sealed class FakeBrowser(Action<Uri> open) : IBrowser
{
    public Uri? Opened { get; private set; }

    public void Open(Uri address)
    {
        Opened = address;
        open(address);
    }
}
