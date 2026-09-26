using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Localization;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Storage;
using Vizstrap.Localization;
using Vizstrap.Mods;
using Vizstrap.Views;
using Vizstrap.Views.Loading;

namespace Vizstrap.ViewModels;

/// <param name="Code">Language code, or null to follow the system.</param>
public sealed record LanguageOption(string? Code, string Name)
{
    public static IReadOnlyList<LanguageOption> All()
    {
        string systemLanguage = SupportedLanguages.All
            .First(language => language.Code == SupportedLanguages.Resolve(null, App.SystemUICulture))
            .NativeName;

        return
        [
            new LanguageOption(null, string.Format(Strings.Settings_LanguageSystem, systemLanguage)),
            .. SupportedLanguages.All.Select(language => new LanguageOption(language.Code, language.NativeName)),
        ];
    }

    public static LanguageOption Find(IReadOnlyList<LanguageOption> options, string? code) =>
        options.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase)) ?? options[0];
}

public sealed record ThemeOption(AppTheme Value, string Name)
{
    public static IReadOnlyList<ThemeOption> All() =>
    [
        new(AppTheme.System, Strings.Theme_System),
        new(AppTheme.Light, Strings.Theme_Light),
        new(AppTheme.Dark, Strings.Theme_Dark),
    ];
}

/// <param name="Swatch">The accent itself, for the colour dot in the picker.</param>
public sealed record AccentOption(AccentColor Value, string Name, Brush Swatch)
{
    public static IReadOnlyList<AccentOption> All() =>
    [
        Create(AccentColor.Violet, Strings.Accent_Violet),
        Create(AccentColor.Blue, Strings.Accent_Blue),
        Create(AccentColor.Teal, Strings.Accent_Teal),
        Create(AccentColor.Green, Strings.Accent_Green),
        Create(AccentColor.Pink, Strings.Accent_Pink),
        Create(AccentColor.Red, Strings.Accent_Red),
        Create(AccentColor.Orange, Strings.Accent_Orange),
    ];

    private static AccentOption Create(AccentColor accent, string name)
    {
        var (r, g, b) = AccentPalette.Tint(0x7F, 0x77, 0xDD, accent);
        var swatch = new SolidColorBrush(Color.FromRgb(r, g, b));
        swatch.Freeze();
        return new AccentOption(accent, name, swatch);
    }
}

public sealed record StyleOption(LoadingStyle Value, string Name)
{
    /// <param name="editable">Include the styles that need the Appearance page's editors (Custom, XML theme).</param>
    public static IReadOnlyList<StyleOption> All(bool editable = true) =>
    [
        new(LoadingStyle.Neon, "Vizstrap (Neon)"),
        new(LoadingStyle.Fluent, "Vizstrap (Fluent)"),
        new(LoadingStyle.FluentClassic, $"Vizstrap ({Strings.Style_Classic})"),
        new(LoadingStyle.Compact, $"Vizstrap ({Strings.Style_Compact})"),
        new(LoadingStyle.Roblox2014, "Roblox (~2014)"),
        new(LoadingStyle.Byfron, "Fake Byfron (2023)"),
        new(LoadingStyle.Legacy2011, "Legacy (2011 – 2014)"),
        new(LoadingStyle.Legacy2008, "Legacy (2008 – 2011)"),
        new(LoadingStyle.Vista, "Vista (2008 – 2011)"),
        .. editable ? new StyleOption[] { new(LoadingStyle.Custom, Strings.Style_Custom), new(LoadingStyle.XmlTheme, Strings.Style_XmlTheme) } : [],
    ];
}

/// <param name="Image">Thumbnail for the picker; null for a custom icon that isn't set yet.</param>
public sealed record IconOption(LoadingIcon Value, string Name, ImageSource? Image);

/// <summary>
/// Backs every settings page. Like Bloxstrap, edits stay pending until "Save"; folder and uninstall
/// actions happen right away. The theme is previewed live and reverted if the changes are discarded.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string BloxstrapUrl = "https://github.com/bloxstraplabs/bloxstrap";

    private readonly JsonStore<State> _state = new(App.Paths.StateFile);
    private readonly SelfInstaller _installer = SelfInstaller.ForCurrentUser(App.Paths);

    private Settings _saved = null!;
    private bool _savedForceReinstall;
    private bool _savedDesktopShortcut;
    private bool _savedStartMenuShortcut;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private AccentOption _selectedAccent;

    [ObservableProperty]
    private bool _robloxAccentTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomStyle), nameof(IsXmlThemeStyle))]
    private StyleOption _selectedStyle;

    [ObservableProperty]
    private IReadOnlyList<IconOption> _icons;

    [ObservableProperty]
    private IconOption _selectedIcon;

    [ObservableProperty]
    private string _loadingTitle;

    [ObservableProperty]
    private string? _customIconPath;

    [ObservableProperty]
    private bool _forceReinstall;

    [ObservableProperty]
    private bool _backgroundUpdates;

    [ObservableProperty]
    private bool _multiInstance;

    [ObservableProperty]
    private bool _confirmLaunches;

    [ObservableProperty]
    private bool _desktopShortcut;

    [ObservableProperty]
    private bool _startMenuShortcut;

    [ObservableProperty]
    private string? _statusMessage;

    public SettingsViewModel()
    {
        _state.Load();
        var settings = App.Settings.Value;

        Languages = LanguageOption.All();
        Themes = ThemeOption.All();
        Accents = AccentOption.All();
        Styles = StyleOption.All();
        CanManageShortcuts = _installer.IsInstalled;

        _selectedLanguage = LanguageOption.Find(Languages, settings.Language);
        _selectedTheme = Themes.First(option => option.Value == settings.Theme);
        _selectedAccent = Accents.FirstOrDefault(option => option.Value == settings.Accent) ?? Accents[0];
        _robloxAccentTheme = settings.RobloxAccentTheme;
        _selectedStyle = Styles.FirstOrDefault(option => option.Value == settings.LoadingStyle) ?? Styles[0];
        _customIconPath = settings.CustomIconPath;
        _icons = BuildIconOptions(_customIconPath);
        _selectedIcon = _icons.FirstOrDefault(option => option.Value == settings.LoadingIcon) ?? _icons[0];
        _loadingTitle = settings.LoadingTitle;
        _forceReinstall = _state.Value.ForceReinstall;
        _backgroundUpdates = settings.BackgroundUpdates;
        _multiInstance = settings.MultiInstance;
        _confirmLaunches = settings.ConfirmLaunches;
        _desktopShortcut = CanManageShortcuts && _installer.HasDesktopShortcut;
        _startMenuShortcut = CanManageShortcuts && _installer.HasStartMenuShortcut;
        TakeSnapshot();

        RobloxVersion = _state.Value.PlayerVersion ?? Strings.Settings_RobloxNotInstalled;
        VersionText = string.Format(Strings.Menu_Version, App.Version);

        Integrations.PropertyChanged += (_, _) => StatusMessage = null;
        LoadingThemes.PropertyChanged += (_, _) => StatusMessage = null;
        Mods.PropertyChanged += (_, _) => StatusMessage = null;
        Effects.PropertyChanged += (_, _) => StatusMessage = null;
        Packages.PropertyChanged += (_, _) => StatusMessage = null;
        Engine.PropertyChanged += (_, _) => StatusMessage = null;
    }

    /// <summary>The "Custom" style's editor and the XML themes, saved together with everything else.</summary>
    public LoadingThemesViewModel LoadingThemes { get; } = new();

    public bool IsCustomStyle => SelectedStyle.Value == LoadingStyle.Custom;

    public bool IsXmlThemeStyle => SelectedStyle.Value == LoadingStyle.XmlTheme;

    /// <summary>The "Integrations" page, saved together with everything else.</summary>
    public IntegrationsViewModel Integrations { get; } = new();

    /// <summary>The "Mods" page, saved together with everything else.</summary>
    public ModsViewModel Mods { get; } = new();

    /// <summary>The picture effects on the "Shaders" page, saved together with everything else.</summary>
    public EffectsViewModel Effects { get; } = new();

    /// <summary>Mod packages on the "Mods" page; which ones are on is saved with everything else.</summary>
    public PackagesViewModel Packages { get; } = new();

    /// <summary>The "Playtime" page: only shows, nothing to save.</summary>
    public PlaytimeViewModel Playtime { get; } = new();

    /// <summary>The "Engine settings" page and Fast Flag editor, saved together with everything else.</summary>
    public EngineSettingsViewModel Engine { get; } = new();

    public IReadOnlyList<LanguageOption> Languages { get; }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public IReadOnlyList<AccentOption> Accents { get; }

    public IReadOnlyList<StyleOption> Styles { get; }

    public bool CanManageShortcuts { get; }

    public string RobloxVersion { get; }

    public string VersionText { get; }

    public string VizstrapFolder => App.Paths.Base;

    public bool IsDirty =>
        SelectedLanguage.Code != _saved.Language ||
        SelectedTheme.Value != _saved.Theme ||
        SelectedAccent.Value != _saved.Accent ||
        RobloxAccentTheme != _saved.RobloxAccentTheme ||
        SelectedStyle.Value != _saved.LoadingStyle ||
        SelectedIcon.Value != _saved.LoadingIcon ||
        LoadingTitle != _saved.LoadingTitle ||
        CustomIconPath != _saved.CustomIconPath ||
        ForceReinstall != _savedForceReinstall ||
        BackgroundUpdates != _saved.BackgroundUpdates ||
        MultiInstance != _saved.MultiInstance ||
        ConfirmLaunches != _saved.ConfirmLaunches ||
        DesktopShortcut != _savedDesktopShortcut ||
        StartMenuShortcut != _savedStartMenuShortcut ||
        Integrations.IsDirty ||
        LoadingThemes.IsDirty ||
        Mods.IsDirty ||
        Effects.IsDirty ||
        Packages.IsDirty ||
        Engine.IsDirty;

    public bool UninstallRequested { get; private set; }

    public bool ReinstallRequested { get; private set; }

    /// <summary>The loading window as it would look with the current, possibly unsaved, choices.</summary>
    public LoadingAppearance PreviewAppearance => LoadingAppearance.From(
        SelectedStyle.Value, SelectedIcon.Value, LoadingTitle, CustomIconPath, App.IsDarkTheme,
        LoadingThemes.Current(), LoadingThemes.SelectedXmlTheme?.Id);

    public event EventHandler? CloseRequested;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // any new edit makes an earlier "Settings saved" stale
        if (e.PropertyName is not (nameof(StatusMessage) or nameof(Icons)))
            StatusMessage = null;
    }

    partial void OnSelectedThemeChanged(ThemeOption value) => App.ApplyTheme(value.Value, SelectedAccent.Value);

    partial void OnSelectedAccentChanged(AccentOption value) => App.ApplyTheme(SelectedTheme.Value, value.Value);

    /// <summary>Puts the saved theme and accent back after the user closes without saving.</summary>
    public void DiscardPreview()
    {
        if (SelectedTheme.Value != _saved.Theme || SelectedAccent.Value != _saved.Accent)
            App.ApplyTheme(_saved.Theme, _saved.Accent);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = App.Settings.Value;
        settings.Language = SelectedLanguage.Code;
        settings.Theme = SelectedTheme.Value;
        settings.Accent = SelectedAccent.Value;
        settings.RobloxAccentTheme = RobloxAccentTheme;
        settings.BackgroundUpdates = BackgroundUpdates;
        settings.MultiInstance = MultiInstance;
        settings.ConfirmLaunches = ConfirmLaunches;
        settings.LoadingStyle = SelectedStyle.Value;
        settings.LoadingIcon = SelectedIcon.Value;
        settings.LoadingTitle = string.IsNullOrWhiteSpace(LoadingTitle) ? Settings.DefaultLoadingTitle : LoadingTitle.Trim();
        settings.CustomIconPath = string.IsNullOrWhiteSpace(CustomIconPath) ? null : CustomIconPath.Trim();
        App.Settings.Save();

        // re-read first: a launch running meanwhile may have updated the state
        _state.Load();
        _state.Value.ForceReinstall = ForceReinstall;
        _state.Save();

        if (CanManageShortcuts)
        {
            _installer.SetDesktopShortcut(DesktopShortcut);
            _installer.SetStartMenuShortcut(StartMenuShortcut);
        }

        LoadingTitle = settings.LoadingTitle;
        CustomIconPath = settings.CustomIconPath;
        TakeSnapshot();

        Integrations.Save();
        LoadingThemes.Save();
        Effects.Save();
        Packages.Save();
        Engine.Save();
        App.Settings.Save();

        try
        {
            RobloxTheme.Apply(settings.RobloxAccentTheme, settings.Accent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(nameof(SettingsViewModel), ex);
        }

        bool modsSaved = await Mods.SaveAsync();
        StatusMessage = modsSaved ? Strings.Settings_Saved : null;

        Log.Info(nameof(SettingsViewModel),
            $"Saved (language: {settings.Language ?? "system"}, theme: {settings.Theme}, style: {settings.LoadingStyle}, icon: {settings.LoadingIcon}, reinstall: {ForceReinstall})");
    }

    [RelayCommand]
    private void BrowseIcon()
    {
        var dialog = new OpenFileDialog { Filter = Strings.Settings_IconFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        if (IconCatalog.TryLoadFile(dialog.FileName) is null)
        {
            MessageWindow.ShowError(Strings.Settings_CustomIconInvalid, null);
            return;
        }

        CustomIconPath = dialog.FileName;
        Icons = BuildIconOptions(CustomIconPath);

        // choosing a file clearly means wanting to use it
        SelectedIcon = Icons.First(option => option.Value == LoadingIcon.Custom);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenRobloxFolder()
    {
        string? version = _state.Value.PlayerVersion;
        string folder = version is not null ? App.Paths.VersionDirectory(version) : App.Paths.Versions;
        OpenFolder(Directory.Exists(folder) ? folder : App.Paths.Base);
    }

    [RelayCommand]
    private void OpenLogs() => OpenFolder(Path.GetDirectoryName(Log.FilePath) ?? App.Paths.Logs);

    [RelayCommand]
    private void OpenVizstrapFolder() => OpenFolder(App.Paths.Base);

    [RelayCommand]
    private void OpenBloxstrap() => Process.Start(new ProcessStartInfo(BloxstrapUrl) { UseShellExecute = true })?.Dispose();

    [RelayCommand]
    private void Uninstall()
    {
        UninstallRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Reinstall()
    {
        ReinstallRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<IconOption> BuildIconOptions(string? customIconPath)
    {
        IconOption Builtin(LoadingIcon icon, string name) => new(icon, name, IconCatalog.Load(icon, null).Image);

        return
        [
            Builtin(LoadingIcon.Vizstrap, "Vizstrap"),
            Builtin(LoadingIcon.Roblox2008, "2008"),
            Builtin(LoadingIcon.Roblox2011, "2011"),
            Builtin(LoadingIcon.RobloxEarly2015, Strings.Icon_Early2015),
            Builtin(LoadingIcon.RobloxLate2015, Strings.Icon_Late2015),
            Builtin(LoadingIcon.Roblox2017, "2017"),
            Builtin(LoadingIcon.Roblox2019, "2019"),
            Builtin(LoadingIcon.Roblox2022, "2022"),
            new(LoadingIcon.Custom, Strings.Icon_Custom, IconCatalog.TryLoadFile(customIconPath)?.Image),
        ];
    }

    private void TakeSnapshot()
    {
        _saved = new Settings
        {
            Language = SelectedLanguage.Code,
            Theme = SelectedTheme.Value,
            Accent = SelectedAccent.Value,
            RobloxAccentTheme = RobloxAccentTheme,
            BackgroundUpdates = BackgroundUpdates,
            MultiInstance = MultiInstance,
            ConfirmLaunches = ConfirmLaunches,
            LoadingStyle = SelectedStyle.Value,
            LoadingIcon = SelectedIcon.Value,
            LoadingTitle = LoadingTitle,
            CustomIconPath = CustomIconPath,
        };
        _savedForceReinstall = ForceReinstall;
        _savedDesktopShortcut = DesktopShortcut;
        _savedStartMenuShortcut = StartMenuShortcut;
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
    }
}
