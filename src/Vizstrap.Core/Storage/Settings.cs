using Vizstrap.Core.Appearance;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Integrations;

namespace Vizstrap.Core.Storage;

/// <summary>User preferences (Settings.json).</summary>
public sealed class Settings
{
    public const string DefaultLoadingTitle = "Vizstrap";

    /// <summary>Language code from <see cref="Localization.SupportedLanguages"/>; null follows the system language.</summary>
    public string? Language { get; set; }

    /// <summary>Dark by default: the Neon look was the brand choice, "System" is opt-in.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>The colour of Vizstrap's windows; violet is the Neon brand colour.</summary>
    public AccentColor Accent { get; set; } = AccentColor.Violet;

    /// <summary>Roblox's loading spinners and logos redrawn in the accent colour (a mod, see ModPresets).</summary>
    public bool RobloxAccentTheme { get; set; } = true;

    public LoadingStyle LoadingStyle { get; set; } = LoadingStyle.Neon;

    public LoadingIcon LoadingIcon { get; set; } = LoadingIcon.Vizstrap;

    /// <summary>Name shown by the loading window (title bar, taskbar).</summary>
    public string LoadingTitle { get; set; } = DefaultLoadingTitle;

    /// <summary>The "Custom" loading style's look.</summary>
    public CustomLoadingTheme CustomTheme { get; set; } = new();

    /// <summary>The picture effects drawn over Roblox (off until switched on in Mods).</summary>
    public EffectSettings Effects { get; set; } = new();

    /// <summary>Ids of the mod packages switched on (see Packages.PackageStore).</summary>
    public List<string> EnabledPackages { get; set; } = [];

    /// <summary>What the player set on packages' tabs: package id → input id → value (see Packages.PackagePages).</summary>
    public Dictionary<string, Dictionary<string, string>> PackageValues { get; set; } = [];

    /// <summary>The XML theme (folder name under CustomThemes) used by the "XmlTheme" style.</summary>
    public string? XmlTheme { get; set; }

    /// <summary>.ico used when <see cref="LoadingIcon"/> is <see cref="Appearance.LoadingIcon.Custom"/>.</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>
    /// When Roblox has a new version, launch the installed one and download the new one while playing
    /// (Bloxstrap's background updates; only for small steps, see Install.BackgroundUpdates).
    /// </summary>
    public bool BackgroundUpdates { get; set; } = true;

    /// <summary>Every launch opens another Roblox instead of replacing the game being played (see Launch.SingletonGuard).</summary>
    public bool MultiInstance { get; set; }

    /// <summary>Without <see cref="MultiInstance"/>: ask before a launch closes the game being played (Bloxstrap's "ConfirmLaunches").</summary>
    public bool ConfirmLaunches { get; set; } = true;

    /// <summary>Whether the Fast Flags configured in Vizstrap reach Roblox (Bloxstrap's "Allow Bloxstrap to manage Fast Flags").</summary>
    public bool UseFastFlagManager { get; set; } = true;

    /// <summary>Follow Roblox's log to know the game being played; the integrations below need it.</summary>
    public bool EnableActivityTracking { get; set; } = true;

    /// <summary>Look up where the game server is (ipinfo.io) and say so when joining.</summary>
    public bool ShowServerLocation { get; set; }

    /// <summary>Close Roblox when leaving a game instead of going back to its desktop app.</summary>
    public bool DisableDesktopApp { get; set; }

    public bool UseDiscordRichPresence { get; set; } = true;

    /// <summary>A "Join server" button on the Discord profile.</summary>
    public bool AllowActivityJoining { get; set; }

    /// <summary>The Roblox account's picture and name on the Discord profile.</summary>
    public bool ShowAccountOnProfile { get; set; }

    /// <summary>"Powered by Vizstrap" under the game on the Discord profile.</summary>
    public bool ShowVizstrapWatermark { get; set; } = true;

    /// <summary>A presence while Roblox is open outside a game (its home page, avatar editor…).</summary>
    public bool ShowPresenceInMenu { get; set; } = true;

    /// <summary>
    /// The user's own Discord application, which carries the Vizstrap logos; null uses the built-in one
    /// (Bloxstrap's, with only a Roblox logo).
    /// </summary>
    public string? DiscordApplicationId { get; set; }

    public List<CustomIntegration> CustomIntegrations { get; set; } = [];
}
