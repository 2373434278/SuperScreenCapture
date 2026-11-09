using System.Globalization;

namespace SSC.Application.Color;

public static class ColorFormats
{
    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string ToRgb(byte r, byte g, byte b) =>
        string.Format(CultureInfo.InvariantCulture, "rgb({0}, {1}, {2})", r, g, b);

    public static string ToHsl(byte r, byte g, byte b)
    {
        var red = r / 255d;
        var green = g / 255d;
        var blue = b / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var lightness = (max + min) / 2d;

        double hue;
        double saturation;

        if (delta < 0.0001)
        {
            hue = 0;
            saturation = 0;
        }
        else
        {
            saturation = lightness > 0.5 ? delta / (2d - max - min) : delta / (max + min);
            if (max == red)
            {
                hue = ((green - blue) / delta + (green < blue ? 6 : 0)) / 6d;
            }
            else if (max == green)
            {
                hue = ((blue - red) / delta + 2) / 6d;
            }
            else
            {
                hue = ((red - green) / delta + 4) / 6d;
            }
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "hsl({0:0}, {1:0}%, {2:0}%)",
            hue * 360,
            saturation * 100,
            lightness * 100);
    }

    public static bool TryParseHex(string? text, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = (r, g, b);
        return true;
    }
}
