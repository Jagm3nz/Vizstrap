namespace Vizstrap.Core.Appearance;

/// <summary>The colour Vizstrap's windows are tinted with; Neon's violet is the brand default.</summary>
public enum AccentColor
{
    Violet,
    Blue,
    Teal,
    Green,
    Pink,
    Red,
    Orange,
}

/// <summary>
/// Every Neon palette colour (backgrounds, borders, text, accent) is a shade of one violet. Another
/// accent turns them all to its hue while keeping their saturation and lightness, so the palette keeps
/// its contrast and look in any colour.
/// </summary>
public static class AccentPalette
{
    /// <summary>The hue of Neon's accent, #7F77DD.</summary>
    private const double VioletHue = 244.7;

    public static double HueOf(AccentColor accent) => accent switch
    {
        AccentColor.Blue => 212,
        AccentColor.Teal => 178,
        AccentColor.Green => 145,
        AccentColor.Pink => 318,
        AccentColor.Red => 352,
        AccentColor.Orange => 24,
        _ => VioletHue,
    };

    /// <summary>A palette colour in the accent's hue; violet leaves it exactly as it is.</summary>
    public static (byte R, byte G, byte B) Tint(byte r, byte g, byte b, AccentColor accent)
    {
        if (accent == AccentColor.Violet)
            return (r, g, b);

        var (hue, saturation, lightness) = ToHsl(r, g, b);
        return FromHsl((hue + HueOf(accent) - VioletHue + 360) % 360, saturation, lightness);
    }

    internal static (double Hue, double Saturation, double Lightness) ToHsl(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2;
        double delta = max - min;

        if (delta == 0)
            return (0, 0, lightness);

        double saturation = delta / (1 - Math.Abs(2 * lightness - 1));

        double hue = max == red ? 60 * ((green - blue) / delta % 6) :
            max == green ? 60 * ((blue - red) / delta + 2) :
            60 * ((red - green) / delta + 4);

        return ((hue + 360) % 360, saturation, lightness);
    }

    internal static (byte R, byte G, byte B) FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - chroma / 2;

        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

        return (Channel(red + m), Channel(green + m), Channel(blue + m));
    }
}
