namespace Hoboman.Core.Settings;

// The size is the window's normal one, so un-maximizing after a restart gives it back.
// A split is the first pane's share of the room, by the name of the place it splits.
public sealed record WindowLayout(double Width, double Height, bool IsMaximized, double SidebarWidth, IReadOnlyDictionary<string, double>? Splits = null);
