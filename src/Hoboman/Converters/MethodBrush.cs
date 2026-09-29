using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hoboman.Converters;

public sealed class MethodBrush : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource($"Method{(value as string)?.ToUpperInvariant()}") ?? Application.Current.TryFindResource("Text");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
