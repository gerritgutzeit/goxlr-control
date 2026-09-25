using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GoXlrControl.App;

public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var page = value?.ToString();
        var wanted = parameter?.ToString();
        return string.Equals(page, wanted, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
