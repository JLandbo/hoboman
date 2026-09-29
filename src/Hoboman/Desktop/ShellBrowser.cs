using System.Diagnostics;
using Hoboman.Core.Auth;

namespace Hoboman.Desktop;

public sealed class ShellBrowser : IBrowser
{
    public void Open(Uri address) => Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
}
