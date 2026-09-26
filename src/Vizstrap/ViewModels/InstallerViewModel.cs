using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vizstrap.Core;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Storage;
using Vizstrap.Localization;
using Vizstrap.Views;

namespace Vizstrap.ViewModels;

public enum InstallerStep
{
    /// <summary>A first install: welcome and language.</summary>
    Start,

    /// <summary>A first install: where to, and which shortcuts.</summary>
    Folder,

    /// <summary>Running a newer Vizstrap.exe over an installed one.</summary>
    Update,

    Installing,

    /// <summary>An update is done (a first install goes on to the first-run wizard instead).</summary>
    Done,
}

/// <summary>
/// The installer: Vizstrap.exe run from anywhere but its install folder, like the file downloaded from a
/// release. A first install picks the language and the folder (<see cref="InstallFolder"/>), then the installed
/// copy carries on with the first-run wizard; over an installed Vizstrap it's an update in place.
/// </summary>
public sealed partial class InstallerViewModel : ObservableObject
{
    private const string LogSource = nameof(InstallerViewModel);

    private const int FirstInstallSteps = 3;

    private readonly string? _installedVersion;
    private readonly bool _alreadyInstalled;
    private LanguageOption _selectedLanguage;
    private InstallFolderProblem _folderProblem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartStep), nameof(IsFolderStep), nameof(IsUpdateStep), nameof(IsInstallingStep), nameof(IsDone),
        nameof(StepText), nameof(StepProgress), nameof(ShowsSteps))]
    private InstallerStep _step;

    [ObservableProperty]
    private string _installFolder;

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    /// <param name="fresh">Behave as on a first install (wizard included) even when Vizstrap is installed.</param>
    public InstallerViewModel(bool fresh = false)
    {
        var installer = SelfInstaller.ForCurrentUser(App.Paths);
        Languages = LanguageOption.All();
        _selectedLanguage = LanguageOption.Find(Languages, App.Settings.Value.Language);
        _alreadyInstalled = installer.IsInstalled;
        _installedVersion = !fresh && _alreadyInstalled ? installer.InstalledVersion : null;

        // an installed Vizstrap stays where it is (its Roblox, mods and settings are there)
        _installFolder = _alreadyInstalled ? App.Paths.Base : VizstrapPaths.DefaultBase;
        _step = IsUpdate ? InstallerStep.Update : InstallerStep.Start;

        if (_alreadyInstalled)
        {
            _createDesktopShortcut = installer.HasDesktopShortcut;
            _createStartMenuShortcut = installer.HasStartMenuShortcut;
        }

        CheckFolder();
    }

    public IReadOnlyList<LanguageOption> Languages { get; private set; }

    /// <summary>Picking a language closes the window so the flow can reopen it translated.</summary>
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || value == _selectedLanguage)
                return;

            _selectedLanguage = value;
            App.Settings.Value.Language = value.Code;
            App.ApplyLanguage(value.Code);

            // option names like "System (Polski)" are translated too
            Languages = LanguageOption.All();
            _selectedLanguage = LanguageOption.Find(Languages, value.Code);

            LanguageChangeRequested = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool LanguageChangeRequested { get; set; }

    public bool LaunchRobloxRequested { get; private set; }

    /// <summary>After a first install: where Vizstrap went, so its installed copy can carry on with the first-run wizard.</summary>
    public VizstrapPaths? InstalledTo { get; private set; }

    public event EventHandler? CloseRequested;

    public bool IsUpdate => _installedVersion is not null;

    public string? UpdateInfo => IsUpdate ? string.Format(Strings.Installer_UpdateInfo, _installedVersion, App.Version) : null;

    public bool IsStartStep => Step == InstallerStep.Start;

    public bool IsFolderStep => Step == InstallerStep.Folder;

    public bool IsUpdateStep => Step == InstallerStep.Update;

    public bool IsInstallingStep => Step == InstallerStep.Installing;

    public bool IsDone => Step == InstallerStep.Done;

    /// <summary>A first install shows "Step n of 3" (the first-run wizard counts its own).</summary>
    public bool ShowsSteps => !IsUpdate && Step is InstallerStep.Start or InstallerStep.Folder or InstallerStep.Installing;

    public string StepText => string.Format(Strings.Welcome_StepOf, StepNumber, FirstInstallSteps);

    public double StepProgress => StepNumber / (double)FirstInstallSteps;

    /// <summary>Only a first install picks its folder.</summary>
    public bool CanChangeFolder => !_alreadyInstalled;

    public string? FolderNote => _alreadyInstalled ? Strings.Installer_FolderFixed : null;

    public string SpaceText => Core.Platform.InstallFolder.FreeSpace(InstallFolder) is { } free
        ? string.Format(Strings.Installer_Space, Bytes(Core.Platform.InstallFolder.RequiredBytes), Bytes(free), Path.GetPathRoot(InstallFolder)?.TrimEnd('\\'))
        : "";

    public string? FolderProblemText => _folderProblem switch
    {
        InstallFolderProblem.NotEmpty => Strings.Installer_FolderNotEmpty,
        InstallFolderProblem.NotWritable => Strings.Installer_FolderNotWritable,
        InstallFolderProblem.LowSpace => Strings.Installer_FolderLowSpace,
        _ => null,
    };

    public bool CanInstall => _folderProblem is InstallFolderProblem.None || (_alreadyInstalled && _folderProblem is not InstallFolderProblem.NotWritable);

    private int StepNumber => Step switch
    {
        InstallerStep.Start => 1,
        InstallerStep.Folder => 2,
        _ => 3,
    };

    partial void OnInstallFolderChanged(string value) => CheckFolder();

    [RelayCommand]
    private void Next() => Step = InstallerStep.Folder;

    [RelayCommand]
    private void Back() => Step = InstallerStep.Start;

    [RelayCommand]
    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Installer_Location,
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(InstallFolder)) ? Path.GetDirectoryName(InstallFolder) : null,
        };

        if (dialog.ShowDialog() == true)
            InstallFolder = Core.Platform.InstallFolder.FromPicked(dialog.FolderName);
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!App.IsSingleFileBuild)
        {
            MessageWindow.ShowError(Strings.Installer_DevBuild, null);
            return;
        }

        var before = Step;
        Step = InstallerStep.Installing;

        // the progress shows before the work, which runs on this thread (shortcuts are COM)
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            InstallFiles();
        }
        catch (IOException ex) when (IsUpdate && File.Exists(App.Paths.InstalledExecutable))
        {
            // the installed copy is running: a window, a launch in progress, or its tray icon while Roblox is open
            Log.Error(LogSource, ex);
            ReplaceRunningCopy(ex, before);
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
            Step = before;
        }
    }

    private void InstallFiles()
    {
        var target = new VizstrapPaths(InstallFolder);
        var state = new JsonStore<State>(target.StateFile);
        state.Load();

        SelfInstaller.ForCurrentUser(target).Install(Environment.ProcessPath!, App.Version, CreateDesktopShortcut, state.Value.PlayerSizeKb, CreateStartMenuShortcut);
        SaveSettingsTo(target);

        if (IsUpdate)
        {
            Step = InstallerStep.Done;
            return;
        }

        InstalledTo = target;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The language picked here goes with Vizstrap, wherever it was installed.</summary>
    private static void SaveSettingsTo(VizstrapPaths target)
    {
        if (string.Equals(target.SettingsFile, App.Paths.SettingsFile, StringComparison.OrdinalIgnoreCase))
        {
            App.Settings.Save();
            return;
        }

        var settings = new JsonStore<Settings>(target.SettingsFile);
        settings.Load();
        settings.Value.Language = App.Settings.Value.Language;
        settings.Save();
    }

    /// <summary>Offers to close the running installed copy (Roblox itself keeps running), then tries again.</summary>
    private void ReplaceRunningCopy(IOException error, InstallerStep before)
    {
        var running = ProcessPaths.FindRunningUnder("Vizstrap", App.Paths.Base)
            .Where(processId => processId != Environment.ProcessId)
            .ToList();

        if (running.Count == 0 || !MessageWindow.Confirm(Strings.Installer_InUseTitle, Strings.Installer_InUse, Strings.Installer_CloseAndContinue, Strings.Common_Cancel))
        {
            if (running.Count == 0)
                MessageWindow.ShowError(Strings.Installer_InUse, error);

            Step = before;
            return;
        }

        foreach (int processId in running)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                Log.Info(LogSource, $"Closing the running Vizstrap (PID {processId}) to replace it");
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // already gone
            }
        }

        try
        {
            InstallFiles();
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Installer_InUse, ex);
            Step = before;
        }
    }

    private void CheckFolder()
    {
        _folderProblem = Core.Platform.InstallFolder.Check(InstallFolder);
        OnPropertyChanged(nameof(FolderProblemText));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(SpaceText));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private static string Bytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} GB", bytes / (1024.0 * 1024 * 1024))
        : string.Format(CultureInfo.CurrentCulture, "{0:0} MB", bytes / (1024.0 * 1024));

    [RelayCommand]
    private void LaunchRoblox()
    {
        LaunchRobloxRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
