using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GoXlrControl.Config;

namespace GoXlrControl.App;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = FaderAccentColours.Normalize(value as string) ?? "2EC4B6";
        return new SolidColorBrush(Color.FromRgb(
            System.Convert.ToByte(hex[..2], 16),
            System.Convert.ToByte(hex[2..4], 16),
            System.Convert.ToByte(hex[4..6], 16)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
