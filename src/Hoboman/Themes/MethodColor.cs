using System.Windows;
using System.Windows.Documents;

namespace Hoboman.Themes;

// The text gets the colour of its request method from the theme, and follows when the theme changes. Another word, as a step's badge, gets the text colour.
public static class MethodColor
{
    public static readonly DependencyProperty OfProperty = DependencyProperty.RegisterAttached("Of", typeof(string), typeof(MethodColor),
        new PropertyMetadata(null, (target, e) => ((FrameworkElement)target).SetResourceReference(TextElement.ForegroundProperty, KeyOf(e.NewValue as string))));

    public static string? GetOf(DependencyObject target) => (string?)target.GetValue(OfProperty);

    public static void SetOf(DependencyObject target, string? value) => target.SetValue(OfProperty, value);

    static string KeyOf(string? method) => $"Method{method?.ToUpperInvariant()}" is var key && Application.Current.TryFindResource(key) is not null ? key : "Text";
}
