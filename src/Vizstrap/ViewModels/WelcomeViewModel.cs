using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vizstrap.Core.FastFlags;
using Vizstrap.Core.Logging;
using Vizstrap.Localization;

namespace Vizstrap.ViewModels;

public enum GraphicsProfile
{
    Unchanged,
    Performance,
    Quality,
}

public sealed record ProfileOption(GraphicsProfile Value, string Name);

/// <summary>
/// The first-run wizard: look, Discord, Roblox, done. Colours change live; everything is saved when
/// the wizard closes, however it's closed.
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    public const int StepCount = 4;

    private const string LogSource = nameof(WelcomeViewModel);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText), nameof(IsFirstStep), nameof(IsLastStep), nameof(IsDiscordStep), nameof(IsRobloxStep), nameof(StepProgress))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _step;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private AccentOption _selectedAccent;

    [ObservableProperty]
    private StyleOption _selectedStyle;

    [ObservableProperty]
    private bool _useDiscordRichPresence;

    [ObservableProperty]
    private bool _showVizstrapWatermark;

    [ObservableProperty]
    private bool _showPresenceInMenu;

    [ObservableProperty]
    private bool _allowActivityJoining;

    [ObservableProperty]
    private bool _robloxAccentTheme;

    [ObservableProperty]
    private bool _backgroundUpdates;

    [ObservableProperty]
    private ProfileOption _selectedProfile;

    public WelcomeViewModel()
    {
        var settings = App.Settings.Value;

        Themes = ThemeOption.All();
        Accents = AccentOption.All();
        Styles = StyleOption.All(editable: false);
        Profiles =
        [
            new(GraphicsProfile.Unchanged, Strings.Welcome_ProfileUnchanged),
            new(GraphicsProfile.Performance, Strings.Flags_ProfilePerformance),
            new(GraphicsProfile.Quality, Strings.Flags_ProfileQuality),
        ];

        _selectedTheme = Themes.First(option => option.Value == settings.Theme);
        _selectedAccent = Accents.First(option => option.Value == settings.Accent);
        _selectedStyle = Styles.FirstOrDefault(option => option.Value == settings.LoadingStyle) ?? Styles[0];
        _useDiscordRichPresence = settings.UseDiscordRichPresence;
        _showVizstrapWatermark = settings.ShowVizstrapWatermark;
        _showPresenceInMenu = settings.ShowPresenceInMenu;
        _allowActivityJoining = settings.AllowActivityJoining;
        _robloxAccentTheme = settings.RobloxAccentTheme;
        _backgroundUpdates = settings.BackgroundUpdates;
        _selectedProfile = Profiles[0];
    }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public IReadOnlyList<AccentOption> Accents { get; }

    public IReadOnlyList<StyleOption> Styles { get; }

    public IReadOnlyList<ProfileOption> Profiles { get; }

    public string StepText => string.Format(Strings.Welcome_StepOf, Step + 1, StepCount);

    public bool IsFirstStep => Step == 0;

    public bool IsLastStep => Step == StepCount - 1;

    public bool IsDiscordStep => Step == 1;

    public bool IsRobloxStep => Step == 2;

    /// <summary>For the bar at the top: how far through the wizard this step is.</summary>
    public double StepProgress => (Step + 1) / (double)StepCount;

    public bool LaunchRobloxRequested { get; private set; }

    public event EventHandler? CloseRequested;

    partial void OnSelectedThemeChanged(ThemeOption value) => App.ApplyTheme(value.Value, SelectedAccent.Value);

    partial void OnSelectedAccentChanged(AccentOption value) => App.ApplyTheme(SelectedTheme.Value, value.Value);

    // Discord's extras only make sense with Discord on
    partial void OnUseDiscordRichPresenceChanged(bool value)
    {
        if (value)
            return;

        ShowVizstrapWatermark = false;
        ShowPresenceInMenu = false;
        AllowActivityJoining = false;
    }

    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount - 1)
            Step++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoBack() => Step > 0;

    [RelayCommand]
    private void LaunchRoblox()
    {
        LaunchRobloxRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Writes the choices; called once, when the wizard closes.</summary>
    public void Save()
    {
        var settings = App.Settings.Value;
        settings.Theme = SelectedTheme.Value;
        settings.Accent = SelectedAccent.Value;
        settings.LoadingStyle = SelectedStyle.Value;
        settings.UseDiscordRichPresence = UseDiscordRichPresence;
        settings.ShowVizstrapWatermark = ShowVizstrapWatermark;
        settings.ShowPresenceInMenu = ShowPresenceInMenu;
        settings.AllowActivityJoining = AllowActivityJoining;
        settings.RobloxAccentTheme = RobloxAccentTheme;
        settings.BackgroundUpdates = BackgroundUpdates;

        // Discord needs to know the game being played
        if (UseDiscordRichPresence)
            settings.EnableActivityTracking = true;

        App.Settings.Save();

        if (SelectedProfile.Value != GraphicsProfile.Unchanged)
        {
            var file = new FastFlagFile(App.Paths);
            var flags = file.Load();

            if (SelectedProfile.Value == GraphicsProfile.Performance)
                FastFlagPresets.ApplyPerformanceProfile(flags);
            else
                FastFlagPresets.ApplyQualityProfile(flags);

            file.Save(flags);
        }

        Log.Info(LogSource, $"Welcome done (theme {settings.Theme}, accent {settings.Accent}, style {settings.LoadingStyle}, " +
            $"Discord {settings.UseDiscordRichPresence}, profile {SelectedProfile.Value}, launch {LaunchRobloxRequested})");
    }
}
