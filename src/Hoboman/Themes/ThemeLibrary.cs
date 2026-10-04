using System.IO;
using System.Windows.Media;

namespace Hoboman.Themes;

// The default theme and the themes in the themes folder, read again each time they are listed, so a file put there shows at once.
public sealed class ThemeLibrary(string folder)
{
    public static Theme Default { get; } = new("Sort og gul", new Dictionary<string, Color>());

    public string Folder => folder;

    public IEnumerable<Theme> Themes =>
        [Default, .. Own.Where(theme => !theme.Name.Equals(Default.Name, StringComparison.OrdinalIgnoreCase)).OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)];

    // The theme in use. The app puts its colours in place when it changes, as it does the texts of a language.
    public Theme Current { get; private set; } = Default;

    public event Action? Changed;

    public void Use(Theme theme)
    {
        Current = theme;
        Changed?.Invoke();
    }

    public Theme Find(string? name) => Themes.FirstOrDefault(theme => theme.Name == name) ?? Default;

    IEnumerable<Theme> Own => Files.Select(Theme.Read).OfType<Theme>();

    string[] Files
    {
        get
        {
            try
            {
                return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }
}
