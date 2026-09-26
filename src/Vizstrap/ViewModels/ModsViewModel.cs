using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vizstrap.Core.Install;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Mods;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Storage;
using Vizstrap.Localization;
using Vizstrap.Views;

namespace Vizstrap.ViewModels;

public sealed record CursorOption(CursorStyle Value, string Name);

public sealed record EmojiOption(EmojiStyle Value, string Name);

public sealed record DeathSoundOption(DeathSound Value, string Name);

/// <summary>
/// The "Mods" settings page, like Bloxstrap's. Presets stay pending until the settings are saved; they
/// only change files in the Modifications folder, which reach Roblox the next time it's launched.
/// </summary>
public sealed partial class ModsViewModel : ObservableObject
{
    /// <summary>Vizstrap's mods folder works exactly like Bloxstrap's, so its guide applies as-is.</summary>
    public const string HelpUrl = "https://bloxstraplabs.com/wiki/features/modding/";

    private const string LogSource = nameof(ModsViewModel);

    private readonly ModPresets _presets = new(App.Paths);

    private CursorStyle _savedCursor;
    private DeathSound _savedDeathSound;
    private bool _savedOldAvatarBackground;
    private bool _savedOldCharacterSounds;
    private EmojiStyle _savedEmoji;
    private bool _savedHasFont;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomCursor))]
    private CursorOption _selectedCursor;

    /// <summary>A newly chosen cursor picture waiting to be saved.</summary>
    [ObservableProperty]
    private string? _newCursorFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDeathSound))]
    private DeathSoundOption _selectedDeathSound;

    /// <summary>A newly chosen death sound waiting to be saved.</summary>
    [ObservableProperty]
    private string? _newDeathSoundFile;

    [ObservableProperty]
    private bool _oldAvatarBackground;

    [ObservableProperty]
    private bool _oldCharacterSounds;

    [ObservableProperty]
    private EmojiOption _selectedEmoji;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoFont))]
    private bool _hasFont;

    /// <summary>A newly chosen font waiting to be saved; null when the font doesn't change.</summary>
    [ObservableProperty]
    private string? _newFontFile;

    public ModsViewModel()
    {
        Cursors =
        [
            new(CursorStyle.Default, Strings.Cursor_Default),
            new(CursorStyle.From2006, Strings.Cursor_2006),
            new(CursorStyle.From2013, Strings.Cursor_2013),
            new(CursorStyle.Custom, Strings.Cursor_Custom),
        ];
        DeathSounds =
        [
            new(DeathSound.Default, Strings.DeathSound_Default),
            new(DeathSound.Muted, Strings.DeathSound_Muted),
            new(DeathSound.Custom, Strings.DeathSound_Custom),
        ];
        Emojis =
        [
            new(EmojiStyle.Default, Strings.Emoji_Default),
            new(EmojiStyle.Catmoji, "Catmoji"),
            new(EmojiStyle.Windows11, "Windows 11"),
            new(EmojiStyle.Windows10, "Windows 10"),
            new(EmojiStyle.Windows8, "Windows 8"),
            new(EmojiStyle.OpenMoji, "OpenMoji"),
            new(EmojiStyle.EmojiTwo, "EmojiTwo"),
        ];

        _selectedCursor = Cursors.First(option => option.Value == _presets.Cursor);
        _selectedDeathSound = DeathSounds.First(option => option.Value == _presets.DeathSound);
        _oldAvatarBackground = _presets.OldAvatarBackground;
        _oldCharacterSounds = _presets.OldCharacterSounds;
        _selectedEmoji = Emojis.First(option => option.Value == _presets.Emoji);
        _hasFont = _presets.HasCustomFont;
        TakeSnapshot();
    }

    public IReadOnlyList<CursorOption> Cursors { get; }

    public IReadOnlyList<EmojiOption> Emojis { get; }

    public IReadOnlyList<DeathSoundOption> DeathSounds { get; }

    public bool IsCustomCursor => SelectedCursor.Value == CursorStyle.Custom;

    public bool IsCustomDeathSound => SelectedDeathSound.Value == DeathSound.Custom;

    public bool HasNoFont => !HasFont;

    public bool IsDirty =>
        SelectedCursor.Value != _savedCursor ||
        NewCursorFile is not null ||
        SelectedDeathSound.Value != _savedDeathSound ||
        NewDeathSoundFile is not null ||
        OldAvatarBackground != _savedOldAvatarBackground ||
        OldCharacterSounds != _savedOldCharacterSounds ||
        SelectedEmoji.Value != _savedEmoji ||
        HasFont != _savedHasFont ||
        NewFontFile is not null;

    /// <summary>Writes the chosen presets into the Modifications folder; false (after telling the user) if something failed.</summary>
    public async Task<bool> SaveAsync()
    {
        _presets.SetCursor(SelectedCursor.Value, NewCursorFile);
        _presets.SetDeathSound(SelectedDeathSound.Value, NewDeathSoundFile);
        NewCursorFile = null;
        NewDeathSoundFile = null;
        _presets.SetOldAvatarBackground(OldAvatarBackground);
        _presets.SetOldCharacterSounds(OldCharacterSounds);

        if (NewFontFile is not null)
            _presets.SetCustomFont(NewFontFile);
        else if (!HasFont && _savedHasFont)
            _presets.SetCustomFont(null);

        NewFontFile = null;
        bool success = true;

        try
        {
            await _presets.SetEmojiAsync(SelectedEmoji.Value, App.Http);
        }
        catch (EmojiDownloadException ex)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Mods_EmojiError, ex);

            // show what's really in the folder
            SelectedEmoji = Emojis.First(option => option.Value == _presets.Emoji);
            success = false;
        }

        TakeSnapshot();
        Log.Info(LogSource, $"Presets saved (cursor: {SelectedCursor.Value}, death sound: {SelectedDeathSound.Value}, avatar background: {OldAvatarBackground}, sounds: {OldCharacterSounds}, emoji: {SelectedEmoji.Value}, font: {HasFont})");
        return success;
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        Directory.CreateDirectory(App.Paths.Modifications);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { App.Paths.Modifications } })?.Dispose();
    }

    [RelayCommand]
    private void OpenHelp() => Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true })?.Dispose();

    /// <summary>Windows' own Compatibility tab for the installed Roblox (DPI scaling, fullscreen optimisations…).</summary>
    [RelayCommand]
    private void OpenCompatibilitySettings()
    {
        var state = new JsonStore<State>(App.Paths.StateFile);
        state.Load();

        string? version = state.Value.PlayerVersion;
        string? executable = version is null ? null : Path.Combine(App.Paths.VersionDirectory(version), RobloxUpdater.PlayerExecutableName);

        if (executable is null || !File.Exists(executable))
        {
            MessageWindow.ShowInfo(Strings.Mods_RobloxNotInstalled);
            return;
        }

        ShellProperties.ShowCompatibility(executable, App.SystemUICulture);
    }

    // choosing "Custom" without a file of one's own asks for the file right away; cancelling goes back
    partial void OnSelectedCursorChanged(CursorOption? oldValue, CursorOption newValue)
    {
        if (newValue.Value == CursorStyle.Custom && _presets.Cursor != CursorStyle.Custom && NewCursorFile is null)
            AskAfterSelection(ChooseCursor, () => SelectedCursor = oldValue ?? Cursors[0], () => NewCursorFile is not null);
    }

    partial void OnSelectedDeathSoundChanged(DeathSoundOption? oldValue, DeathSoundOption newValue)
    {
        if (newValue.Value == DeathSound.Custom && _presets.DeathSound != DeathSound.Custom && NewDeathSoundFile is null)
            AskAfterSelection(ChooseDeathSound, () => SelectedDeathSound = oldValue ?? DeathSounds[0], () => NewDeathSoundFile is not null);
    }

    /// <summary>Opens the file dialog once the combo box has finished changing its selection.</summary>
    private static void AskAfterSelection(Action ask, Action revert, Func<bool> chosen) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ask();

            if (!chosen())
                revert();
        });

    [RelayCommand]
    private void ChooseCursor()
    {
        var dialog = new OpenFileDialog { Filter = Strings.Mods_CursorFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        if (!ModPresets.IsPngFile(dialog.FileName))
        {
            MessageWindow.ShowError(Strings.Mods_CursorInvalid, null);
            return;
        }

        NewCursorFile = dialog.FileName;
        SelectedCursor = Cursors.First(option => option.Value == CursorStyle.Custom);
    }

    [RelayCommand]
    private void ChooseDeathSound()
    {
        var dialog = new OpenFileDialog { Filter = Strings.Mods_SoundFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        if (!ModPresets.IsSoundFile(dialog.FileName))
        {
            MessageWindow.ShowError(Strings.Mods_SoundInvalid, null);
            return;
        }

        NewDeathSoundFile = dialog.FileName;
        SelectedDeathSound = DeathSounds.First(option => option.Value == DeathSound.Custom);
    }

    [RelayCommand]
    private void ChooseFont()
    {
        var dialog = new OpenFileDialog { Filter = Strings.Mods_FontFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        if (!ModPresets.IsFontFile(dialog.FileName))
        {
            MessageWindow.ShowError(Strings.Mods_FontInvalid, null);
            return;
        }

        NewFontFile = dialog.FileName;
        HasFont = true;
    }

    [RelayCommand]
    private void RemoveFont()
    {
        NewFontFile = null;
        HasFont = false;
    }

    private void TakeSnapshot()
    {
        _savedCursor = SelectedCursor.Value;
        _savedDeathSound = SelectedDeathSound.Value;
        _savedOldAvatarBackground = OldAvatarBackground;
        _savedOldCharacterSounds = OldCharacterSounds;
        _savedEmoji = SelectedEmoji.Value;
        _savedHasFont = HasFont;
    }
}
