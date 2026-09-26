using Microsoft.Win32;

namespace Vizstrap.Core.Appearance;

/// <summary>Colour theme of Vizstrap's own windows (Bloxstrap's "Global theme").</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Look of the window shown while Roblox is updated and started (Bloxstrap's "Style").</summary>
public enum LoadingStyle
{
    /// <summary>Vizstrap's own, always dark.</summary>
    Neon,
    /// <summary>Windows 11 style, big icon in the middle (Bloxstrap's default "Bloxstrap").</summary>
    Fluent,
    /// <summary>Windows 11 style with a title bar and a footer (Bloxstrap Classic).</summary>
    FluentClassic,
    /// <summary>A small bar.</summary>
    Compact,
    /// <summary>The Roblox bootstrapper of about 2014.</summary>
    Roblox2014,
    /// <summary>Roblox's 2023 bootstrapper ("Fake Byfron" in Bloxstrap).</summary>
    Byfron,
    Legacy2011,
    Legacy2008,
    Vista,
    /// <summary>Put together in Vizstrap's editor: background, colours, layout (see CustomLoadingTheme).</summary>
    Custom,
    /// <summary>A theme in Bloxstrap's XML format (see XmlThemes).</summary>
    XmlTheme,
}

/// <summary>Icon of the loading window and its taskbar button (Bloxstrap's "Icon").</summary>
public enum LoadingIcon
{
    Vizstrap,
    Roblox2008,
    Roblox2011,
    RobloxEarly2015,
    RobloxLate2015,
    Roblox2017,
    Roblox2019,
    Roblox2022,
    Custom,
}

public static class LoadingStyles
{
    /// <summary>Styles that imitate old Windows dialogs and always stay light, as in Bloxstrap.</summary>
    public static bool IgnoresTheme(LoadingStyle style) =>
        style is LoadingStyle.Legacy2008 or LoadingStyle.Legacy2011 or LoadingStyle.Vista;
}

public static class ThemeResolver
{
    public const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Whether the given theme ends up dark, following Windows' app mode for <see cref="AppTheme.System"/>.</summary>
    public static bool IsDark(AppTheme theme, Func<bool> systemUsesLightApps) => theme switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => !systemUsesLightApps(),
    };

    /// <summary>Windows' "Choose your app mode" setting; light when it can't be read.</summary>
    public static bool SystemUsesLightApps(RegistryKey? currentUser = null)
    {
        using var key = (currentUser ?? Registry.CurrentUser).OpenSubKey(PersonalizeKeyPath);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
