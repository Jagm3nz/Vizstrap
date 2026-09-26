using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Vizstrap.Core;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Localization;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Packages;
using Vizstrap.Core.Storage;
using Vizstrap.Localization;
using Vizstrap.Views;
using Wpf.Ui.Appearance;

namespace Vizstrap;

public partial class App : Application
{
    private const string LogSource = nameof(App);

    /// <summary>The Windows display language, captured before Vizstrap switches the UI culture.</summary>
    public static CultureInfo SystemUICulture { get; } = CultureInfo.CurrentUICulture;

    public static VizstrapPaths Paths { get; } = VizstrapPaths.Default;

    public static JsonStore<Settings> Settings { get; } = new(Paths.SettingsFile);

    /// <summary>Mod packages made by players (see Core.Packages).</summary>
    public static PackageStore Packages { get; } = new(Paths.Packages);

    /// <summary>The installed packages the player switched on.</summary>
    public static IReadOnlyList<InstalledPackage> EnabledPackages() =>
        [.. Packages.List().Where(package => Settings.Value.EnabledPackages.Contains(package.Id))];

    // must stay above Http, which puts it in the User-Agent (static initializers run in order)
    public static string Version { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static HttpClient Http { get; } = CreateHttpClient();

    /// <summary>True when this process is %LocalAppData%\Vizstrap\Vizstrap.exe rather than a downloaded copy.</summary>
    public static bool IsInstalledCopy => Paths.IsInstalledExecutable(Environment.ProcessPath);

    /// <summary>Single-file builds report an empty assembly location; only those can install themselves.</summary>
#pragma warning disable IL3000 // the empty location is exactly what's being tested for
    public static bool IsSingleFileBuild => string.IsNullOrEmpty(typeof(App).Assembly.Location);
#pragma warning restore IL3000

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool useInstallLogs = IsInstalledCopy || Directory.Exists(Paths.Base);
        Log.Initialize(useInstallLogs ? Paths.Logs : Path.Combine(Path.GetTempPath(), "Vizstrap", "Logs"));
        Log.Info(LogSource, $"Vizstrap {Version} from {Environment.ProcessPath}, args: {string.Join(' ', e.Args)}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error(LogSource, (Exception)args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(LogSource, args.Exception);
            args.SetObserved();
        };

        Settings.Load();
        ApplyLanguage(Settings.Value.Language);
        ApplyTheme(Settings.Value.Theme, Settings.Value.Accent);

        int exitCode =
#if DEBUG
            e.Args is ["-testwatcher", var processId, var logFile, var discordPipe]
                ? Integrations.Watcher.RunForTest(int.Parse(processId, CultureInfo.InvariantCulture), logFile, discordPipe) :
#endif
            Flows.Run(LaunchArgs.Parse(e.Args));

        Log.Info(LogSource, $"Exiting with code {exitCode}");
        Log.Shutdown();
        Shutdown(exitCode);
    }

    public static string LanguageCode { get; private set; } = SupportedLanguages.Fallback;

    public static void ApplyLanguage(string? preferred)
    {
        LanguageCode = SupportedLanguages.Resolve(preferred, SystemUICulture);

        var culture = new CultureInfo(LanguageCode);
        Strings.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        Log.Info(LogSource, $"UI language: {LanguageCode} (saved: {preferred ?? "system"}, system: {SystemUICulture.Name})");
    }

    /// <summary>Whether Vizstrap's windows are currently dark (the resolved <see cref="AppTheme"/>).</summary>
    public static bool IsDarkTheme { get; private set; } = true;

    /// <summary>The accent Vizstrap's windows are tinted with right now (the saved one, or a preview).</summary>
    public static AccentColor Accent { get; private set; } = AccentColor.Violet;

    /// <summary>
    /// Switches WPF-UI's theme and the Neon palette, live: open windows follow through DynamicResource.
    /// The single-colour accent overload derives near-white shades for dark mode, so every shade is set
    /// explicitly, with white text on top.
    /// </summary>
    public static void ApplyTheme(AppTheme theme, AccentColor accent)
    {
        var app = (App)Current;
        IsDarkTheme = ThemeResolver.IsDark(theme, () => ThemeResolver.SystemUsesLightApps());
        Accent = accent;

        void ApplyAccentColors() => ApplicationAccentColorManager.Apply(
            systemAccent: Tint(Color.FromRgb(0x7F, 0x77, 0xDD), accent),
            primaryAccent: Tint(Color.FromRgb(0x67, 0x5E, 0xD3), accent),
            secondaryAccent: Tint(Color.FromRgb(0x78, 0x70, 0xDA), accent),
            tertiaryAccent: Tint(Color.FromRgb(0x5A, 0x51, 0xC8), accent));

        // the reloaded theme copies the accent colours set *before* it into its brushes (buttons…),
        // and the accent manager sets some brushes of its own, so the accent goes in both before and after
        ApplyAccentColors();
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
            Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);

        var palettes = app.Resources.MergedDictionaries;
        int paletteIndex = palettes.ToList().FindIndex(dictionary => dictionary.Contains("NeonBackgroundBrush"));
        palettes[paletteIndex] = CreatePalette(IsDarkTheme, accent);

        ApplyAccentColors();

        var background = (Color)app.Resources["NeonBackgroundColor"];
        app.Resources["ApplicationBackgroundColor"] = background;
        app.Resources["ApplicationBackgroundBrush"] = new SolidColorBrush(background);
        app.Resources["TextOnAccentFillColorPrimary"] = Colors.White;
        app.Resources["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White);

        foreach (var window in app.Windows.OfType<NeonWindow>())
            window.RefreshBackground();

        Log.Info(LogSource, $"Theme: {theme} ({(IsDarkTheme ? "dark" : "light")}), accent: {accent}");
    }

    /// <summary>The Neon palette (Themes/Palette.Dark or .Light) in the accent's colour.</summary>
    public static ResourceDictionary CreatePalette(bool dark, AccentColor accent)
    {
        var source = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/Palette.{(dark ? "Dark" : "Light")}.xaml") };
        var palette = new ResourceDictionary();

        foreach (System.Collections.DictionaryEntry entry in source)
        {
            palette[entry.Key] = entry.Value switch
            {
                SolidColorBrush brush => Frozen(new SolidColorBrush(Tint(brush.Color, accent))),
                Color color => Tint(color, accent),
                var other => other,
            };
        }

        return palette;
    }

    private static Color Tint(Color color, AccentColor accent)
    {
        var (r, g, b) = AccentPalette.Tint(color.R, color.G, color.B, accent);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(LogSource, e.Exception);
        e.Handled = true;

        MessageWindow.ShowError(Strings.Error_Unexpected, e.Exception);

        Log.Shutdown();
        Shutdown(1);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Vizstrap/{Version}");
        return client;
    }
}
