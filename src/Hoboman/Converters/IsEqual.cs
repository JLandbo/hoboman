using System.Globalization;
using System.Windows.Data;

namespace Hoboman.Converters;

public sealed class IsEqual : IValueConverter, IMultiValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? parameter : Binding.DoNothing;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) => Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
