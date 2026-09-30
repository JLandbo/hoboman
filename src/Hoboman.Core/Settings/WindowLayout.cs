namespace Hoboman.Core.Settings;

// The size is the window's normal one, so un-maximizing after a restart gives it back.
public sealed record WindowLayout(double Width, double Height, bool IsMaximized, double SidebarWidth);
