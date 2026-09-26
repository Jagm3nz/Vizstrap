using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vizstrap.Core.Integrations;
using Vizstrap.Localization;
using Vizstrap.Mods;

namespace Vizstrap.ViewModels;

/// <summary>
/// The "Integrations" settings page, like Bloxstrap's: activity tracking, Discord Rich Presence and
/// custom integrations. Everything stays pending until the settings are saved.
/// </summary>
public sealed partial class IntegrationsViewModel : ObservableObject
{
    private SavedIntegrations _saved = null!;

    [ObservableProperty]
    private bool _enableActivityTracking;

    [ObservableProperty]
    private bool _showServerLocation;

    [ObservableProperty]
    private bool _disableDesktopApp;

    [ObservableProperty]
    private bool _useDiscordRichPresence;

    [ObservableProperty]
    private bool _allowActivityJoining;

    [ObservableProperty]
    private bool _showAccountOnProfile;

    [ObservableProperty]
    private bool _showVizstrapWatermark;

    [ObservableProperty]
    private bool _showPresenceInMenu;

    /// <summary>The own Discord application's id as typed; saved only when it looks like one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscordApplicationIdValid))]
    private string _discordApplicationId = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteIntegrationCommand), nameof(BrowseLocationCommand))]
    private CustomIntegration? _selectedIntegration;

    public IntegrationsViewModel()
    {
        var settings = App.Settings.Value;

        _enableActivityTracking = settings.EnableActivityTracking;
        _showServerLocation = settings.ShowServerLocation;
        _disableDesktopApp = settings.DisableDesktopApp;
        _useDiscordRichPresence = settings.UseDiscordRichPresence;
        _allowActivityJoining = settings.AllowActivityJoining;
        _showAccountOnProfile = settings.ShowAccountOnProfile;
        _showVizstrapWatermark = settings.ShowVizstrapWatermark;
        _showPresenceInMenu = settings.ShowPresenceInMenu;
        _discordApplicationId = settings.DiscordApplicationId ?? "";

        foreach (var integration in settings.CustomIntegrations)
            Add(integration.Clone());

        _selectedIntegration = Integrations.FirstOrDefault();
        TakeSnapshot();
    }

    /// <summary>Edited copies; the saved list changes only on <see cref="Save"/>.</summary>
    public ObservableCollection<CustomIntegration> Integrations { get; } = [];

    public bool HasSelection => SelectedIntegration is not null;

    /// <summary>Empty (the built-in application) or a Discord snowflake: 17–20 digits.</summary>
    public bool IsDiscordApplicationIdValid
    {
        get
        {
            string id = DiscordApplicationId.Trim();
            return id.Length == 0 || (id.Length is >= 17 and <= 20 && id.All(char.IsAsciiDigit));
        }
    }

    public bool IsDirty =>
        EnableActivityTracking != _saved.EnableActivityTracking ||
        ShowServerLocation != _saved.ShowServerLocation ||
        DisableDesktopApp != _saved.DisableDesktopApp ||
        UseDiscordRichPresence != _saved.UseDiscordRichPresence ||
        AllowActivityJoining != _saved.AllowActivityJoining ||
        ShowAccountOnProfile != _saved.ShowAccountOnProfile ||
        ShowVizstrapWatermark != _saved.ShowVizstrapWatermark ||
        ShowPresenceInMenu != _saved.ShowPresenceInMenu ||
        DiscordApplicationId.Trim() != _saved.DiscordApplicationId ||
        Integrations.Count != _saved.Integrations.Count ||
        Integrations.Zip(_saved.Integrations).Any(pair => !pair.First.SameAs(pair.Second));

    // like Bloxstrap, switching a feature off also switches off what depends on it
    partial void OnEnableActivityTrackingChanged(bool value)
    {
        if (value)
            return;

        ShowServerLocation = false;
        DisableDesktopApp = false;
        UseDiscordRichPresence = false;
    }

    partial void OnUseDiscordRichPresenceChanged(bool value)
    {
        if (value)
            return;

        AllowActivityJoining = false;
        ShowAccountOnProfile = false;
    }

    [RelayCommand]
    private void AddIntegration()
    {
        var integration = new CustomIntegration { Name = Strings.Integrations_NewName };
        Add(integration);
        SelectedIntegration = integration;
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteIntegration()
    {
        if (SelectedIntegration is not { } integration)
            return;

        int index = Integrations.IndexOf(integration);
        integration.PropertyChanged -= OnIntegrationChanged;
        Integrations.Remove(integration);

        SelectedIntegration = Integrations.Count == 0 ? null : Integrations[Math.Min(index, Integrations.Count - 1)];
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BrowseLocation()
    {
        if (SelectedIntegration is not { } integration)
            return;

        var dialog = new OpenFileDialog { Filter = Strings.Integrations_ProgramFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        integration.Location = dialog.FileName;

        // a fresh entry gets the program's name, as in Bloxstrap
        if (integration.Name == Strings.Integrations_NewName || string.IsNullOrWhiteSpace(integration.Name))
            integration.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    public void Save()
    {
        var settings = App.Settings.Value;

        settings.EnableActivityTracking = EnableActivityTracking;
        settings.ShowServerLocation = ShowServerLocation;
        settings.DisableDesktopApp = DisableDesktopApp;
        settings.UseDiscordRichPresence = UseDiscordRichPresence;
        settings.AllowActivityJoining = AllowActivityJoining;
        settings.ShowAccountOnProfile = ShowAccountOnProfile;
        settings.ShowVizstrapWatermark = ShowVizstrapWatermark;
        settings.ShowPresenceInMenu = ShowPresenceInMenu;

        if (IsDiscordApplicationIdValid)
            settings.DiscordApplicationId = DiscordApplicationId.Trim() is { Length: > 0 } id ? id : null;

        DiscordApplicationId = settings.DiscordApplicationId ?? "";
        settings.CustomIntegrations = Integrations.Select(integration => integration.Clone()).ToList();

        TakeSnapshot();
    }

    private void Add(CustomIntegration integration)
    {
        integration.PropertyChanged += OnIntegrationChanged;
        Integrations.Add(integration);
    }

    // editing a field of an integration counts as a change to the page
    private void OnIntegrationChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(IsDirty));

    /// <summary>Draws the Vizstrap logos (one per accent) to upload to the own Discord application.</summary>
    [RelayCommand]
    private void PrepareDiscordLogos()
    {
        string folder = RobloxTheme.ExportDiscordLogos(Path.Combine(App.Paths.Base, "DiscordLogos"));
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
    }

    [RelayCommand]
    private void OpenDeveloperPortal() =>
        Process.Start(new ProcessStartInfo(DeveloperPortalUrl) { UseShellExecute = true })?.Dispose();

    public const string DeveloperPortalUrl = "https://discord.com/developers/applications";

    private void TakeSnapshot() => _saved = new SavedIntegrations(
        EnableActivityTracking, ShowServerLocation, DisableDesktopApp, UseDiscordRichPresence,
        AllowActivityJoining, ShowAccountOnProfile, ShowVizstrapWatermark, ShowPresenceInMenu, DiscordApplicationId.Trim(),
        Integrations.Select(integration => integration.Clone()).ToList());

    private sealed record SavedIntegrations(
        bool EnableActivityTracking, bool ShowServerLocation, bool DisableDesktopApp, bool UseDiscordRichPresence,
        bool AllowActivityJoining, bool ShowAccountOnProfile, bool ShowVizstrapWatermark, bool ShowPresenceInMenu,
        string DiscordApplicationId, List<CustomIntegration> Integrations);
}
