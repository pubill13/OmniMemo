using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Memoit.Views;

public sealed class ScrollbarColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = (Color)ColorConverter.ConvertFromString((string)value);
        var brush = new SolidColorBrush(Color.FromRgb((byte)(color.R * .45), (byte)(color.G * .45), (byte)(color.B * .45)));
        brush.Freeze();
        return brush;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
