using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Packages;
using Vizstrap.Localization;
using Vizstrap.Views;

namespace Vizstrap.ViewModels;

/// <summary>One installed package in the list; switching one with a program on asks first.</summary>
public sealed partial class PackageItem(InstalledPackage package, bool enabled, Func<PackageItem, bool> confirmPlugin) : ObservableObject
{
    private bool _enabled = enabled;

    public InstalledPackage Package { get; } = package;

    public string Id => Package.Id;

    public string Title => string.IsNullOrWhiteSpace(Package.Manifest.Version)
        ? Package.Manifest.Name
        : $"{Package.Manifest.Name} {Package.Manifest.Version}";

    public string Details => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(Package.Manifest.Author) ? null : string.Format(Strings.Packages_By, Package.Manifest.Author),
        string.Join(", ", Package.Contents().Select(ContentName)),
    }.Where(part => !string.IsNullOrEmpty(part)));

    public string? Description => Package.Manifest.Description;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            // a program runs with the player's rights: only after saying yes
            if (value && !_enabled && Package.HasPlugin && !confirmPlugin(this))
            {
                OnPropertyChanged();
                return;
            }

            SetProperty(ref _enabled, value);
        }
    }

    private static string ContentName(string content) => content switch
    {
        InstalledPackage.FilesFolder => Strings.Packages_ContentsFiles,
        InstalledPackage.EffectsFolder => Strings.Packages_ContentsEffects,
        InstalledPackage.LoadingThemesFolder => Strings.Packages_ContentsThemes,
        InstalledPackage.PagesFolder => Strings.Packages_ContentsPages,
        _ => Strings.Packages_ContentsPlugin,
    };
}

/// <summary>
/// The "Mod packages" part of the Mods page: installing, making and packing packages happens right away;
/// which ones are on is saved with the other settings.
/// </summary>
public sealed partial class PackagesViewModel : ObservableObject
{
    private const string LogSource = nameof(PackagesViewModel);

    private HashSet<string> _saved = [];

    public PackagesViewModel()
    {
        Reload();
        TakeSnapshot();
    }

    public ObservableCollection<PackageItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool IsDirty => !EnabledIds().SetEquals(_saved);

    public void Save()
    {
        App.Settings.Value.EnabledPackages = [.. EnabledIds().Order(StringComparer.Ordinal)];
        TakeSnapshot();
        Log.Info(LogSource, $"Packages on: {string.Join(", ", App.Settings.Value.EnabledPackages)}");
    }

    [RelayCommand]
    private void InstallFromFile()
    {
        var dialog = new OpenFileDialog { Filter = Strings.Packages_FileFilter, CheckFileExists = true };

        if (dialog.ShowDialog() == true)
            Install(dialog.FileName);
    }

    [RelayCommand]
    private void InstallFromFolder()
    {
        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
            Install(dialog.FolderName);
    }

    [RelayCommand]
    private void NewTemplate()
    {
        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() != true)
            return;

        string folder = FreeFolder(Path.Combine(dialog.FolderName, "My Vizstrap mod"));

        try
        {
            PackageStore.CreateTemplate(folder);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
            MessageWindow.ShowInfo(string.Format(Strings.Packages_TemplateCreated, folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
        }
    }

    [RelayCommand]
    private void PackFolder()
    {
        var folderDialog = new OpenFolderDialog();

        if (folderDialog.ShowDialog() != true)
            return;

        var saveDialog = new SaveFileDialog
        {
            Filter = Strings.Packages_PackFilter,
            FileName = Path.GetFileName(folderDialog.FolderName) + PackageStore.Extension,
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            PackageStore.Pack(folderDialog.FolderName, saveDialog.FileName);
            MessageWindow.ShowInfo(string.Format(Strings.Packages_Packed, saveDialog.FileName));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageWindow.ShowError(string.Format(Strings.Packages_PackFailed, ex.Message), null);
        }
    }

    [RelayCommand]
    private void OpenPackagesFolder()
    {
        Directory.CreateDirectory(App.Packages.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { App.Packages.Folder } })?.Dispose();
    }

    [RelayCommand]
    private void Remove(PackageItem? item)
    {
        if (item is null || !MessageWindow.Confirm(string.Format(Strings.Packages_RemoveQuestion, item.Package.Manifest.Name), "",
                Strings.Packages_Remove, Strings.Common_Cancel, danger: true))
            return;

        try
        {
            App.Packages.Remove(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
        }

        Reload();
    }

    private void Install(string source)
    {
        try
        {
            var installed = App.Packages.Install(source);
            Reload();

            // installing means wanting it; a program still asks
            if (Items.FirstOrDefault(item => item.Id == installed.Id) is { } item)
                item.Enabled = true;

            MessageWindow.ShowInfo(string.Format(Strings.Packages_Installed, installed.Manifest.Name));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogSource, $"Package from {source} not installed: {ex.Message}");
            MessageWindow.ShowError(string.Format(Strings.Packages_InstallFailed, ex.Message), null);
        }
    }

    private void Reload()
    {
        var on = Items.Count > 0 ? EnabledIds() : [.. App.Settings.Value.EnabledPackages];

        foreach (var item in Items)
            item.PropertyChanged -= OnItemChanged;
        Items.Clear();

        foreach (var package in App.Packages.List())
        {
            var item = new PackageItem(package, on.Contains(package.Id), ConfirmPlugin);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Items));

    private static bool ConfirmPlugin(PackageItem item) =>
        MessageWindow.Confirm(Strings.Packages_PluginTitle,
            string.Format(Strings.Packages_PluginWarning, item.Package.Manifest.Name, item.Package.Manifest.Plugin!.Run),
            Strings.Packages_PluginAllow, Strings.Common_Cancel, danger: true);

    private HashSet<string> EnabledIds() => [.. Items.Where(item => item.Enabled).Select(item => item.Id)];

    private void TakeSnapshot() => _saved = EnabledIds();

    private static string FreeFolder(string folder)
    {
        string candidate = folder;

        for (int number = 2; Directory.Exists(candidate); number++)
            candidate = $"{folder} {number}";

        return candidate;
    }
}
