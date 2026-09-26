using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Storage;

namespace Vizstrap.Views.Loading;

/// <summary>Everything a loading window needs to look the way the user chose.</summary>
/// <param name="Image">Big picture shown inside the window.</param>
/// <param name="WindowIcon">Multi-size icon for the title bar and taskbar.</param>
/// <param name="Custom">The "Custom" style's look.</param>
/// <param name="XmlThemeDirectory">The folder of the XML theme, for the "XmlTheme" style.</param>
public sealed record LoadingAppearance(
    LoadingStyle Style, string Title, ImageSource Image, ImageSource WindowIcon, bool IsDark,
    CustomLoadingTheme? Custom = null, string? XmlThemeDirectory = null)
{
    public static LoadingAppearance From(LoadingStyle style, LoadingIcon icon, string? title, string? customIconPath, bool isDark,
        CustomLoadingTheme? custom = null, string? xmlTheme = null)
    {
        var (image, windowIcon) = IconCatalog.Load(icon, customIconPath);
        string finalTitle = string.IsNullOrWhiteSpace(title) ? Settings.DefaultLoadingTitle : title.Trim();
        string? xmlThemeDirectory = style == LoadingStyle.XmlTheme ? XmlThemeDirectoryOf(xmlTheme) : null;

        return new LoadingAppearance(style, finalTitle, image, windowIcon, isDark, custom, xmlThemeDirectory);
    }

    /// <summary>A player's theme folder, or a built-in theme written out in the current accent (Neon when none was picked).</summary>
    private static string? XmlThemeDirectoryOf(string? xmlTheme)
    {
        xmlTheme ??= BuiltinThemes.IdOf(BuiltinThemes.Keys[0]);

        if (PackageThemes.IsPackageTheme(xmlTheme))
            return PackageThemes.DirectoryOf(xmlTheme);

        if (!BuiltinThemes.IsBuiltin(xmlTheme))
            return new XmlThemes(App.Paths.CustomThemes).DirectoryOf(xmlTheme);

        if (BuiltinThemes.KeyOf(xmlTheme) is not { } key)
            return null;

        try
        {
            return BuiltinThemes.WriteTo(App.Paths.BuiltinThemes, key, App.Accent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(nameof(LoadingAppearance), $"Built-in theme {key} can't be written: {ex.Message}");
            return null;
        }
    }

    public static LoadingAppearance FromSettings(Settings settings) => From(
        settings.LoadingStyle, settings.LoadingIcon, settings.LoadingTitle, settings.CustomIconPath, App.IsDarkTheme,
        settings.CustomTheme, settings.XmlTheme);
}

public static class IconCatalog
{
    private const string LogSource = nameof(IconCatalog);

    private static string ResourcePath(LoadingIcon icon) => icon switch
    {
        LoadingIcon.Roblox2008 => "Assets/Icons/Icon2008.ico",
        LoadingIcon.Roblox2011 => "Assets/Icons/Icon2011.ico",
        LoadingIcon.RobloxEarly2015 => "Assets/Icons/IconEarly2015.ico",
        LoadingIcon.RobloxLate2015 => "Assets/Icons/IconLate2015.ico",
        LoadingIcon.Roblox2017 => "Assets/Icons/Icon2017.ico",
        LoadingIcon.Roblox2019 => "Assets/Icons/Icon2019.ico",
        LoadingIcon.Roblox2022 => "Assets/Icons/Icon2022.ico",
        _ => "Assets/Vizstrap.ico",
    };

    /// <summary>
    /// The picture for inside the window and the icon for the taskbar. A custom icon that can't be read
    /// falls back to Vizstrap's, so a deleted file never stops Roblox from launching.
    /// </summary>
    public static (ImageSource Image, ImageSource WindowIcon) Load(LoadingIcon icon, string? customIconPath)
    {
        if (icon == LoadingIcon.Custom)
        {
            var custom = TryLoadFile(customIconPath);

            if (custom is not null)
                return custom.Value;

            Log.Warn(LogSource, $"Custom icon '{customIconPath}' could not be loaded, using Vizstrap's");
            icon = LoadingIcon.Vizstrap;
        }

        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/{ResourcePath(icon)}"))!.Stream;
        var (largest, windowIcon) = Decode(stream);

        // the vector logo stays sharp at any size
        ImageSource image = icon == LoadingIcon.Vizstrap ? (ImageSource)Application.Current.Resources["VizstrapLogo"] : largest;
        return (image, windowIcon);
    }

    /// <summary>Loads an .ico file, or returns null when it's missing or not an icon.</summary>
    public static (ImageSource Image, ImageSource WindowIcon)? TryLoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            return Decode(stream);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            Log.Warn(LogSource, $"Not a usable icon: {path} ({ex.Message})");
            return null;
        }
    }

    private static (BitmapSource Largest, BitmapSource WindowIcon) Decode(Stream stream)
    {
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        // WPF picks the right sizes for the title bar and taskbar from an .ico frame that keeps its decoder
        var windowIcon = decoder.Frames[0];
        var largest = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();

        largest.Freeze();
        return (largest, windowIcon);
    }
}
