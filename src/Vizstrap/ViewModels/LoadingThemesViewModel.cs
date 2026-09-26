using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vizstrap.Core;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Logging;
using Vizstrap.Localization;
using Vizstrap.Views;
using Vizstrap.Views.Loading;

namespace Vizstrap.ViewModels;

public sealed record LayoutOption(ThemeLayout Value, string Name);

public sealed record SizeOption(ThemeSize Value, string Name);

/// <param name="Id">What the settings keep: a folder name, or "builtin:Neon" for a theme that ships with Vizstrap.</param>
public sealed record XmlThemeOption(string Id, string Name, bool IsBuiltin);

/// <summary>
/// The loading window's own looks on the Appearance page: the "Custom" style's editor and the list of
/// XML themes (Bloxstrap's format). Edits wait for Save; theme files are managed right away.
/// </summary>
public sealed partial class LoadingThemesViewModel : ObservableObject
{
    private const string LogSource = nameof(LoadingThemesViewModel);

    private readonly XmlThemes _xmlThemes = new(App.Paths.CustomThemes);
    private CustomLoadingTheme _savedTheme = null!;
    private string? _savedXmlTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackgroundColorValid))]
    private string _backgroundColor = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackgroundImage), nameof(BackgroundImageName))]
    private string? _backgroundImage;

    [ObservableProperty]
    private bool _fillImage;

    [ObservableProperty]
    private int _dim;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextColorValid))]
    private string _textColor = "";

    [ObservableProperty]
    private bool _barUsesAccent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBarColorValid))]
    private string _barColor = "";

    [ObservableProperty]
    private LayoutOption _selectedLayout;

    [ObservableProperty]
    private SizeOption _selectedSize;

    [ObservableProperty]
    private bool _showIcon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasXmlThemeSelection))]
    [NotifyCanExecuteChangedFor(nameof(EditXmlThemeCommand), nameof(DeleteXmlThemeCommand))]
    private XmlThemeOption? _selectedXmlTheme;

    public LoadingThemesViewModel()
    {
        Layouts =
        [
            new(ThemeLayout.Centered, Strings.Layout_Centered),
            new(ThemeLayout.Bottom, Strings.Layout_Bottom),
            new(ThemeLayout.Side, Strings.Layout_Side),
            new(ThemeLayout.Minimal, Strings.Layout_Minimal),
        ];
        Sizes =
        [
            new(ThemeSize.Small, Strings.Size_Small),
            new(ThemeSize.Medium, Strings.Size_Medium),
            new(ThemeSize.Large, Strings.Size_Large),
        ];

        var settings = App.Settings.Value;
        _selectedLayout = Layouts[0];
        _selectedSize = Sizes[1];
        Load(settings.CustomTheme);

        ReloadXmlThemes();
        _selectedXmlTheme = XmlThemeOptions.FirstOrDefault(option => option.Id == settings.XmlTheme) ?? XmlThemeOptions[0];
        TakeSnapshot();
    }

    public IReadOnlyList<LayoutOption> Layouts { get; }

    public IReadOnlyList<SizeOption> Sizes { get; }

    /// <summary>The built-in themes first, then the player's own.</summary>
    public ObservableCollection<XmlThemeOption> XmlThemeOptions { get; } = [];

    public bool IsBackgroundColorValid => CustomLoadingTheme.IsColor(BackgroundColor);

    public bool IsTextColorValid => CustomLoadingTheme.IsColor(TextColor);

    public bool IsBarColorValid => BarUsesAccent || CustomLoadingTheme.IsColor(BarColor);

    public bool HasBackgroundImage => BackgroundImage is not null;

    public string? BackgroundImageName => BackgroundImage is null ? null : Path.GetFileName(BackgroundImage);

    public bool HasXmlThemeSelection => SelectedXmlTheme is not null;

    public bool CanDeleteXmlTheme => SelectedXmlTheme is { IsBuiltin: false };

    public bool CanImportFromBloxstrap => new XmlThemes(VizstrapPaths.BloxstrapCustomThemes).List().Count > 0;

    public bool IsDirty => !Current().SameAs(_savedTheme) || SelectedXmlTheme?.Id != _savedXmlTheme;

    /// <summary>The editor's theme as it would be saved; colours that aren't valid keep the saved ones.</summary>
    public CustomLoadingTheme Current() => new()
    {
        BackgroundColor = IsBackgroundColorValid ? BackgroundColor.Trim() : _savedTheme?.BackgroundColor ?? CustomLoadingTheme.DefaultBackground,
        BackgroundImage = BackgroundImage,
        FillImage = FillImage,
        Dim = Math.Clamp(Dim, 0, CustomLoadingTheme.MaxDim),
        TextColor = IsTextColorValid ? TextColor.Trim() : _savedTheme?.TextColor ?? CustomLoadingTheme.DefaultText,
        BarColor = BarUsesAccent || !CustomLoadingTheme.IsColor(BarColor) ? null : BarColor.Trim(),
        Layout = SelectedLayout.Value,
        Size = SelectedSize.Value,
        ShowIcon = ShowIcon,
    };

    public void Save()
    {
        var settings = App.Settings.Value;
        settings.CustomTheme = Current();
        settings.XmlTheme = SelectedXmlTheme?.Id;
        RemoveUnusedPictures(settings.CustomTheme.BackgroundImage);
        Load(settings.CustomTheme);
        TakeSnapshot();
    }

    // ---- the "Custom" style

    [RelayCommand]
    private void ChooseBackground()
    {
        var dialog = new OpenFileDialog { Filter = Strings.Theme_PictureFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        // kept in Vizstrap's folder, so moving or deleting the original doesn't break the theme
        Directory.CreateDirectory(App.Paths.LoadingBackgrounds);
        string copy = Path.Combine(App.Paths.LoadingBackgrounds, $"{Guid.NewGuid():N}{Path.GetExtension(dialog.FileName)}");
        File.Copy(dialog.FileName, copy);

        BackgroundImage = copy;
        Log.Info(LogSource, $"Background picture {dialog.FileName} copied to {copy}");
    }

    [RelayCommand]
    private void RemoveBackground() => BackgroundImage = null;

    [RelayCommand]
    private void ResetCustomTheme() => Load(new CustomLoadingTheme());

    // ---- XML themes

    [RelayCommand]
    private void NewXmlTheme()
    {
        string name = _xmlThemes.Create(Strings.XmlThemes_NewName);
        ReloadXmlThemes();
        Select(name);
        EditXmlTheme();
    }

    [RelayCommand]
    private void ImportZip()
    {
        var dialog = new OpenFileDialog { Filter = Strings.XmlThemes_ZipFilter, CheckFileExists = true };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string name = _xmlThemes.ImportZip(dialog.FileName);
            ReloadXmlThemes();
            Select(name);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(string.Format(Strings.XmlThemes_ImportFailed, ex.Message), null);
        }
    }

    [RelayCommand]
    private void ImportFromBloxstrap()
    {
        var imported = _xmlThemes.ImportFolder(VizstrapPaths.BloxstrapCustomThemes);
        ReloadXmlThemes();

        if (imported.Count > 0)
            Select(imported[0]);

        MessageWindow.ShowInfo(string.Format(Strings.XmlThemes_Imported, imported.Count));
    }

    /// <summary>Opens the theme in Notepad; a built-in one is first copied into the player's own themes.</summary>
    [RelayCommand(CanExecute = nameof(HasXmlThemeSelection))]
    private void EditXmlTheme()
    {
        if (SelectedXmlTheme is not { } theme)
            return;

        if (BuiltinThemes.KeyOf(theme.Id) is { } key)
        {
            string copy = _xmlThemes.Create(theme.Name, BuiltinThemes.Read(key, App.Accent));
            ReloadXmlThemes();
            Select(copy);
        }
        else if (PackageThemes.DirectoryOf(theme.Id) is { } packageTheme)
        {
            string copy = _xmlThemes.ImportTheme(packageTheme);
            ReloadXmlThemes();
            Select(copy);
        }

        Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { _xmlThemes.FileOf(SelectedXmlTheme!.Id) } })?.Dispose();
    }

    [RelayCommand]
    private void OpenXmlThemesFolder()
    {
        Directory.CreateDirectory(_xmlThemes.Folder);
        string folder = SelectedXmlTheme is { IsBuiltin: false } theme ? _xmlThemes.DirectoryOf(theme.Id) : _xmlThemes.Folder;
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteXmlTheme))]
    private void DeleteXmlTheme()
    {
        if (SelectedXmlTheme is not { IsBuiltin: false } theme ||
            !MessageWindow.Confirm(string.Format(Strings.XmlThemes_DeleteQuestion, theme.Name), "", Strings.XmlThemes_Delete, Strings.Common_Cancel, danger: true))
            return;

        try
        {
            _xmlThemes.Delete(theme.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
        }

        ReloadXmlThemes();
        SelectedXmlTheme = XmlThemeOptions[0];
    }

    [RelayCommand]
    private void OpenXmlThemeExamples() =>
        Process.Start(new ProcessStartInfo(Vizstrap.Core.Appearance.XmlThemes.ExamplesUrl) { UseShellExecute = true })?.Dispose();

    // ---- helpers

    private void Load(CustomLoadingTheme theme)
    {
        BackgroundColor = theme.BackgroundColor;
        BackgroundImage = theme.BackgroundImage;
        FillImage = theme.FillImage;
        Dim = theme.Dim;
        TextColor = theme.TextColor;
        BarUsesAccent = theme.BarColor is null;
        BarColor = theme.BarColor ?? "#FFFFFF";
        SelectedLayout = Layouts.First(option => option.Value == theme.Layout);
        SelectedSize = Sizes.First(option => option.Value == theme.Size);
        ShowIcon = theme.ShowIcon;
    }

    private void ReloadXmlThemes()
    {
        var selected = SelectedXmlTheme;
        XmlThemeOptions.Clear();

        foreach (string key in BuiltinThemes.Keys)
            XmlThemeOptions.Add(new XmlThemeOption(BuiltinThemes.IdOf(key), BuiltinThemes.NameOf(key), IsBuiltin: true));

        // switched-on mod packages' themes, read-only like the built-in ones
        foreach (var package in App.EnabledPackages())
            foreach (string theme in package.LoadingThemes)
                XmlThemeOptions.Add(new XmlThemeOption(PackageThemes.IdOf(package, theme), $"{Path.GetFileName(theme)} ({package.Manifest.Name})", IsBuiltin: true));

        foreach (string name in _xmlThemes.List())
            XmlThemeOptions.Add(new XmlThemeOption(name, name, IsBuiltin: false));

        // clearing the list unselects; keep the choice when it's still there
        if (selected is not null)
            SelectedXmlTheme = XmlThemeOptions.FirstOrDefault(option => option == selected) ?? XmlThemeOptions[0];

        OnPropertyChanged(nameof(CanImportFromBloxstrap));
    }

    private void Select(string id) => SelectedXmlTheme = XmlThemeOptions.FirstOrDefault(option => option.Id == id) ?? XmlThemeOptions[0];

    private void TakeSnapshot()
    {
        _savedTheme = Current();
        _savedXmlTheme = SelectedXmlTheme?.Id;
    }

    /// <summary>Pictures chosen and replaced again are leftovers in Vizstrap's folder.</summary>
    private static void RemoveUnusedPictures(string? inUse)
    {
        if (!Directory.Exists(App.Paths.LoadingBackgrounds))
            return;

        foreach (string file in Directory.GetFiles(App.Paths.LoadingBackgrounds))
        {
            if (string.Equals(file, inUse, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn(LogSource, $"Couldn't remove {file}: {ex.Message}");
            }
        }
    }
}
