using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Hoboman.Controls;

namespace Hoboman.Themes;

// A theme gives some of the colours in Colors.xaml its own, as in hamster-pet: a file in themes\ named after the theme,
// holding {"colors": {"Key": "#AARRGGBB"}}. A colour it leaves out stays the default's.
public sealed record Theme(string Name, IReadOnlyDictionary<string, Color> Colors)
{
    static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    static ResourceDictionary? _defaults;

    // The default theme's colours, also while another theme is in use.
    static ResourceDictionary Defaults => _defaults ??= new() { Source = new("/Hoboman;component/Themes/Colors.xaml", UriKind.Relative) };

    public static Theme? Read(string file)
    {
        try
        {
            return Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static Theme Parse(string name, string json) => new(name, ColorsOf(JsonSerializer.Deserialize<SavedTheme>(json, _options)?.Colors ?? []));

    // Every colour gets a new brush, also those the theme leaves to the default, so all that is drawn with them is drawn again.
    public ResourceDictionary ToResources()
    {
        var resources = new ResourceDictionary();
        foreach (var name in Defaults.Keys.OfType<string>())
        {
            resources[name] = BrushOf(name);
        }
        return resources;
    }

    public Brush BrushOf(string name) => BrushOf(Colors.TryGetValue(name, out var color) ? color : ((SolidColorBrush)Defaults[name]).Color);

    // The editors get their colours first, as putting the theme's colours in place draws them again. Gives the colours put in place.
    internal ResourceDictionary Apply(ResourceDictionary resources, ResourceDictionary? current)
    {
        Colorings.Use(BrushOf);
        var colors = ToResources();
        if (current is not null)
        {
            resources.MergedDictionaries.Remove(current);
        }
        resources.MergedDictionaries.Add(colors);
        return colors;
    }

    static SolidColorBrush BrushOf(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Dictionary<string, Color> ColorsOf(Dictionary<string, string?> colors) =>
        colors.Select(entry => (entry.Key, Color: ColorOf(entry.Value))).Where(entry => entry.Color is not null).ToDictionary(entry => entry.Key, entry => entry.Color!.Value);

    // A colour from a colour profile is left out, as it would have the file point at one on the disk.
    static Color? ColorOf(string? text)
    {
        try
        {
            return text is null || text.TrimStart().StartsWith("ContextColor", StringComparison.OrdinalIgnoreCase) ? null : (Color)ColorConverter.ConvertFromString(text);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    sealed record SavedTheme(Dictionary<string, string?>? Colors);
}
