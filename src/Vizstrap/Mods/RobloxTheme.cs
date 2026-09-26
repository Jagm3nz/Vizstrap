using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Discord;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Mods;

namespace Vizstrap.Mods;

/// <summary>
/// "Vizstrap's colours in Roblox": draws Roblox's loading spinners in the accent colour and puts the
/// Vizstrap logo and name where Roblox has its own. The rest of Roblox's look (backgrounds, buttons)
/// is compiled into its program and can't be changed with files. Also draws the logos for an own
/// Discord application.
/// </summary>
internal static class RobloxTheme
{
    private const string LogSource = nameof(RobloxTheme);

    /// <summary>The "V" mark, in the 100×100 units of Neon.xaml's VizstrapLogo.</summary>
    private static readonly Geometry Square = Geometry.Parse("M22,0 H78 A22,22 0 0 1 100,22 V78 A22,22 0 0 1 78,100 H22 A22,22 0 0 1 0,78 V22 A22,22 0 0 1 22,0 Z");
    private static readonly Geometry Mark = Geometry.Parse("M22.5,26.4 L37.1,26.4 L50,57.6 L62.9,26.4 L77.5,26.4 L56.6,75.2 L43.4,75.2 Z");

    /// <summary>Brings the Modifications folder in line with the setting; redraws only when the accent changed.</summary>
    public static void Apply(bool enabled, AccentColor accent)
    {
        var presets = new ModPresets(App.Paths);

        if (!enabled)
        {
            presets.SetRobloxTheme(null);
            return;
        }

        if (presets.RobloxThemeAccent == accent.ToString())
            return;

        presets.SetRobloxTheme(Render(accent));
        Log.Info(LogSource, $"Roblox's spinners and logos drawn in {accent}");
    }

    /// <summary>Every picture of <see cref="ModPresets.RobloxThemeTargets"/>, labelled with the accent.</summary>
    public static Dictionary<string, byte[]> Render(AccentColor accent)
    {
        var bright = Tint(0x7F, 0x77, 0xDD, accent);
        var logo = Tint(0x53, 0x4A, 0xB7, accent);

        byte[] Picture(string target) => Path.GetFileName(target) switch
        {
            "loadingCircle.png" => Ring(256, 24, bright, Color.FromArgb(0xE6, 0x1B, 0x1B, 0x1B)),
            "LoadingSpinner.png" => Ring(100, 10, bright, null),
            "DarkThemeLoadingCircle.png" => Ring(80, 8, bright, Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            "LightThemeLoadingCircle.png" => Ring(80, 8, bright, Color.FromArgb(0x30, 0x00, 0x00, 0x00)),
            "robloxlogo.png" => Wordmark(256, 256, bright, logo),
            "coloredlogo.png" => Logo(24, logo),
            "coloredlogo@2x.png" => Logo(48, logo),
            "coloredlogo@3x.png" => Logo(72, logo),
            "roblox_logo.png" => Logo(132, logo, margin: 8),
            var other => throw new InvalidOperationException($"No picture for {other}."),
        };

        return ModPresets.RobloxThemeTargets.ToDictionary(
            target => target,
            target => PngText.Add(Picture(target), ModPresets.ThemeMarker, accent.ToString()));
    }

    /// <summary>
    /// The logos to upload to an own Discord application (Rich Presence → Art Assets), one per accent,
    /// named with the image keys Vizstrap uses. Returns the folder.
    /// </summary>
    public static string ExportDiscordLogos(string folder)
    {
        Directory.CreateDirectory(folder);

        foreach (var accent in Enum.GetValues<AccentColor>())
            File.WriteAllBytes(Path.Combine(folder, GamePresence.LogoKey(accent) + ".png"), Logo(1024, Tint(0x53, 0x4A, 0xB7, accent)));

        return folder;
    }

    private static Color Tint(byte r, byte g, byte b, AccentColor accent)
    {
        var (red, green, blue) = AccentPalette.Tint(r, g, b, accent);
        return Color.FromRgb(red, green, blue);
    }

    /// <summary>
    /// A spinner: a 270° arc in the accent, fading towards its tail, over an optional full track.
    /// Drawn pixel by pixel for a smooth angular fade and anti-aliased edges.
    /// </summary>
    internal static byte[] Ring(int size, double thickness, Color accent, Color? track)
    {
        const double sweep = 270;
        double center = size / 2.0;
        double radius = center - thickness / 2 - 1;
        var pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x + 0.5 - center, dy = y + 0.5 - center;
                double coverage = Math.Clamp(thickness / 2 - Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius) + 0.5, 0, 1);

                if (coverage == 0)
                    continue;

                // clockwise from the top
                double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
                double arcAlpha = angle <= sweep ? angle / sweep * coverage : 0;
                double trackAlpha = track is { } t ? t.A / 255.0 * coverage : 0;

                double alpha = arcAlpha + trackAlpha * (1 - arcAlpha);

                if (alpha <= 0)
                    continue;

                byte Mix(byte arc, byte background) =>
                    (byte)Math.Round((arc * arcAlpha + background * trackAlpha * (1 - arcAlpha)) / alpha);

                int i = (y * size + x) * 4;
                var under = track ?? Colors.Transparent;
                pixels[i] = Mix(accent.B, under.B);
                pixels[i + 1] = Mix(accent.G, under.G);
                pixels[i + 2] = Mix(accent.R, under.R);
                pixels[i + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        return Encode(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4));
    }

    /// <summary>The rounded square with the white "V".</summary>
    internal static byte[] Logo(int size, Color color, double margin = 0) => Draw(size, size, context =>
    {
        double scale = (size - 2 * margin) / 100;
        context.PushTransform(new TransformGroup
        {
            Children = { new ScaleTransform(scale, scale), new TranslateTransform(margin, margin) },
        });
        context.DrawGeometry(new SolidColorBrush(color), null, Square);
        context.DrawGeometry(Brushes.White, null, Mark);
        context.Pop();
    });

    /// <summary>"VIZSTRAP" in the chunky, outlined style of Roblox's old red logo.</summary>
    internal static byte[] Wordmark(int width, int height, Color fill, Color outline) => Draw(width, height, context =>
    {
        var text = new FormattedText("VIZSTRAP", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Black"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
            100, Brushes.White, 1.0);

        var geometry = text.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        double scale = (width - 24) / bounds.Width;

        context.PushTransform(new TranslateTransform((width - bounds.Width * scale) / 2 - bounds.X * scale,
            (height - bounds.Height * scale) / 2 - bounds.Y * scale));
        context.PushTransform(new ScaleTransform(scale, scale));

        context.DrawGeometry(null, new Pen(new SolidColorBrush(outline), 16 / scale) { LineJoin = PenLineJoin.Round }, geometry);
        context.DrawGeometry(null, new Pen(Brushes.White, 8 / scale) { LineJoin = PenLineJoin.Round }, geometry);
        context.DrawGeometry(new SolidColorBrush(fill), null, geometry);

        context.Pop();
        context.Pop();
    });

    private static byte[] Draw(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
            draw(context);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Encode(bitmap);
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
