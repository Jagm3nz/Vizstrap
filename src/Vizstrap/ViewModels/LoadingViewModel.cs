using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vizstrap.Core;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Install;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Mods;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Storage;
using Vizstrap.Localization;
using Vizstrap.Mods;
using Vizstrap.Views.Loading;

namespace Vizstrap.ViewModels;

/// <summary>Drives the Neon loading window: update Roblox if needed, then start it.</summary>
public sealed partial class LoadingViewModel : ObservableObject
{
    private const string LogSource = nameof(LoadingViewModel);
    /// <summary>Held while Roblox is updated or started; a background update takes it to switch versions.</summary>
    internal const string BootstrapperLockName = "Vizstrap-Bootstrapper";

    private readonly string? _robloxUri;
    private readonly bool _noLaunch;
    private readonly CancellationTokenSource _cancellation = new();

    [ObservableProperty]
    private string _status = Strings.Loading_Connecting;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private TaskbarItemProgressState _taskbarState = TaskbarItemProgressState.Indeterminate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _canCancel = true;

    private readonly bool _isPreview;

    public LoadingViewModel(string? robloxUri, bool noLaunch, LoadingAppearance appearance)
    {
        _robloxUri = robloxUri;
        _noLaunch = noLaunch;
        Appearance = appearance;
    }

    private LoadingViewModel(LoadingAppearance appearance)
    {
        _isPreview = true;
        Appearance = appearance;
    }

    /// <summary>Plays through every stage with made-up numbers; touches neither the network nor Roblox.</summary>
    public static LoadingViewModel CreatePreview(LoadingAppearance appearance) => new(appearance);

    public LoadingAppearance Appearance { get; }

    public string Title => Appearance.Title;

    public ImageSource IconImage => Appearance.Image;

    public bool IsDark => Appearance.IsDark;

    /// <summary>Set when the run failed (not when it was cancelled).</summary>
    public Exception? Error { get; private set; }

    /// <summary>The Roblox that was started, once it is.</summary>
    public LaunchedRoblox? Launched { get; private set; }

    public bool IsRunning { get; private set; }

    public event EventHandler? Finished;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        Log.Info(LogSource, "Cancelled by user");
        CanCancel = false;
        Status = Strings.Loading_Cancelling;
        _cancellation.Cancel();
    }

    public async Task RunAsync()
    {
        IsRunning = true;
        var token = _cancellation.Token;

        try
        {
            if (_isPreview)
                await SimulateAsync(token);
            else
                await UpdateAndLaunchAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Log.Info(LogSource, "Stopped after cancellation");
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            Error = ex;
        }
        finally
        {
            IsRunning = false;
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SimulateAsync(CancellationToken token)
    {
        const long previewTotal = 250_000_000;
        const int previewPackages = 20;

        ShowIndeterminate(Strings.Loading_Connecting);
        await Task.Delay(900, token);

        ShowIndeterminate(Strings.Loading_CheckingVersion);
        await Task.Delay(700, token);

        for (int percent = 0; percent <= 100; percent += 2)
        {
            OnProgress(new UpdateProgress(UpdateStage.Downloading, IsFreshInstall: false,
                previewTotal * percent / 100, previewTotal, percent * previewPackages / 100, previewPackages, 21_500_000));
            await Task.Delay(45, token);
        }

        ShowIndeterminate(Strings.Loading_Finalizing);
        await Task.Delay(800, token);

        CanCancel = false;
        ShowIndeterminate(Strings.Loading_Starting);
        await Task.Delay(1000, token);
    }

    private async Task UpdateAndLaunchAsync(CancellationToken token)
    {
        var dispatcher = Application.Current.Dispatcher;

        using var bootstrapperLock = await InterProcessLock.AcquireAsync(
            BootstrapperLockName,
            () => dispatcher.BeginInvoke(() => Status = Strings.Loading_Waiting),
            token);

        var state = new JsonStore<State>(App.Paths.StateFile);
        var updater = new RobloxUpdater(
            App.Paths,
            state,
            new RobloxDeployment(App.Http),
            new PackageDownloader(App.Http, App.Paths.Downloads, RobloxPaths.Downloads));

        // "-nolaunch" is there to install, so it never puts an update off
        var result = await updater.EnsureLatestAsync(_noLaunch ? null : (current, latest, cancel) => DeferUpdateAsync(current, latest, state, cancel),
            new Progress<UpdateProgress>(OnProgress), token);
        var installed = result.Installed;

        // still cancellable: the first time the picture effects need their depth AI, it's 50 MB
        await EnsureEffectsModelAsync(token);

        // mods are re-checked on every launch: cheap when nothing changed, and a fresh update needs them all
        CanCancel = false;
        ShowIndeterminate(Strings.Loading_ApplyingMods);

        // drawn with WPF, so here on the UI thread; applied with the other mods below
        try
        {
            RobloxTheme.Apply(App.Settings.Value.RobloxAccentTheme, App.Settings.Value.Accent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogSource, $"Couldn't draw Vizstrap's colours for Roblox: {ex.Message}");
        }
        var packageFiles = App.EnabledPackages().Select(package => package.FilesDirectory).OfType<string>().ToList();
        var mods = await Task.Run(() => new ModApplier(App.Paths, state).Apply(installed.Directory, App.Settings.Value.UseFastFlagManager, packageFiles));

        if (!mods.Success)
            Log.Warn(LogSource, $"Some mods could not be applied: {string.Join(", ", mods.Failed)}");

        ShowIndeterminate(Strings.Loading_Starting);

        // the official client likes to take the links back, so reclaim them on every launch; only a
        // properly installed copy does this, since the installer is what remembers the previous launcher
        var installer = SelfInstaller.ForCurrentUser(App.Paths);

        if (App.IsInstalledCopy && installer.IsInstalled)
        {
            ProtocolRegistrar.ForCurrentUser().RegisterVizstrap(App.Paths.InstalledExecutable);
            installer.UpdateEstimatedSize(state.Value.PlayerSizeKb);
        }

        if (_noLaunch)
            Log.Info(LogSource, $"Ready: {installed.VersionGuid} (not launching, -nolaunch)");
        else
            Launched = await RobloxLauncher.LaunchAsync(installed, _robloxUri, RobloxPaths.Logs, TimeSpan.FromSeconds(15), CancellationToken.None);

        // started once Roblox is on its way, so the download doesn't slow down joining
        if (result.Deferred is not null)
            StartBackgroundUpdate();
    }

    /// <summary>
    /// Bloxstrap's background updates: play the installed version now when the new one is a small step
    /// away. Otherwise the update happens here, after stopping a background update that may be running.
    /// </summary>
    private async Task<bool> DeferUpdateAsync(InstalledRoblox current, ClientVersion latest, JsonStore<State> state, CancellationToken token)
    {
        string reason = "background updates are off";

        if (App.Settings.Value.BackgroundUpdates &&
            BackgroundUpdates.CanDefer(BackgroundUpdates.ReadRelease(current.ExecutablePath), latest.Version,
                RobloxUpdater.FreeSpace(App.Paths.Base), state.Value.ForceReinstall, out reason))
            return true;

        Log.Info(LogSource, $"Updating now: {reason}");

        if (BackgroundUpdates.IsRunning())
        {
            Status = Strings.Loading_StoppingBackgroundUpdate;
            await BackgroundUpdates.StopAsync(TimeSpan.FromSeconds(30), token);
        }

        return false;
    }

    private static void StartBackgroundUpdate()
    {
        string executable = App.IsInstalledCopy ? App.Paths.InstalledExecutable : Environment.ProcessPath!;

        try
        {
            Process.Start(new ProcessStartInfo(executable, "-backgroundupdate") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // the next launch updates as usual
            Log.Warn(LogSource, $"Couldn't start the background update: {ex.Message}");
        }
    }

    private void OnProgress(UpdateProgress progress)
    {
        if (_cancellation.IsCancellationRequested)
            return;

        switch (progress.Stage)
        {
            case UpdateStage.Connecting:
                ShowIndeterminate(Strings.Loading_Connecting);
                break;

            case UpdateStage.CheckingVersion:
                ShowIndeterminate(Strings.Loading_CheckingVersion);
                break;

            case UpdateStage.Downloading:
                double fraction = progress.BytesTotal == 0 ? 0 : (double)progress.BytesDone / progress.BytesTotal;
                string title = progress.IsFreshInstall ? Strings.Loading_Installing : Strings.Loading_Updating;

                Status = $"{title} · {fraction.ToString("P0", CultureInfo.CurrentCulture)}";
                // count the package being downloaded, so the first (and biggest) one reads "1 of 20", not "0 of 20"
                int currentPackage = Math.Min(progress.PackagesDone + 1, progress.PackagesTotal);
                Detail = string.Format(Strings.Loading_Detail,
                    ByteSize.Format(progress.BytesPerSecond), currentPackage, progress.PackagesTotal);
                Progress = fraction;
                IsIndeterminate = false;
                TaskbarState = TaskbarItemProgressState.Normal;
                break;

            case UpdateStage.Finalizing:
                ShowIndeterminate(Strings.Loading_Finalizing);
                break;
        }
    }

    /// <summary>The picture effects' depth AI, downloaded the first time it's needed; the game starts without it if that fails.</summary>
    private async Task EnsureEffectsModelAsync(CancellationToken token)
    {
        var effects = App.Settings.Value.Effects;

        if (_noLaunch || !effects.Enabled || !effects.UseDepth || DepthModel.IsReady(App.Paths.Effects))
            return;

        Status = Strings.Loading_DownloadingEffects;
        Detail = null;
        Progress = 0;
        IsIndeterminate = false;
        TaskbarState = TaskbarItemProgressState.Normal;

        try
        {
            await DepthModel.EnsureAsync(App.Http, App.Paths.Effects, new Progress<double>(fraction =>
            {
                Status = $"{Strings.Loading_DownloadingEffects} · {fraction.ToString("P0", CultureInfo.CurrentCulture)}";
                Progress = fraction;
            }), token);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or InvalidDataException ||
                                   ex is TaskCanceledException && !token.IsCancellationRequested)
        {
            Log.Warn(LogSource, $"The depth AI couldn't be downloaded; effects without depth this time: {ex.Message}");
        }
    }

    private void ShowIndeterminate(string status)
    {
        Status = status;
        Detail = null;
        IsIndeterminate = true;
        TaskbarState = TaskbarItemProgressState.Indeterminate;
    }
}
