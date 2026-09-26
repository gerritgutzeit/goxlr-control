using System.Globalization;
using System.Windows.Media;

namespace GoXlrControl.App.ViewModels;

/// <summary>HSV helpers for the fader accent colour picker.</summary>
internal static class ColourSpace
{
    public static void ToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0.00001)
        {
            h = 0;
            return;
        }

        if (Math.Abs(max - r) < 0.00001)
            h = 60 * (((g - b) / delta) % 6);
        else if (Math.Abs(max - g) < 0.00001)
            h = 60 * (((b - r) / delta) + 2);
        else
            h = 60 * (((r - g) / delta) + 4);

        if (h < 0) h += 360;
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return Color.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    public static string ToHex(Color color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, null, out var r)) return false;
        if (!byte.TryParse(hex[2..4], NumberStyles.HexNumber, null, out var g)) return false;
        if (!byte.TryParse(hex[4..6], NumberStyles.HexNumber, null, out var b)) return false;
        color = Color.FromRgb(r, g, b);
        return true;
    }
}
