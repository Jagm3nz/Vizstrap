using System.Diagnostics;
using Vizstrap.Core;
using Vizstrap.Core.Install;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Storage;
using Vizstrap.Integrations;
using Vizstrap.Localization;
using Vizstrap.ViewModels;
using Vizstrap.Views;
using Vizstrap.Views.Loading;
using Vizstrap.Views.SettingsPages;

namespace Vizstrap;

/// <summary>Every way Vizstrap can be started, as a sequence of windows. Each returns the process exit code.</summary>
internal static class Flows
{
    private const string LogSource = nameof(Flows);

    public static int Run(LaunchArgs args) => args.Command switch
    {
        LaunchCommand.Player => Play(args.RobloxUri, args.NoLaunch),
        LaunchCommand.Settings => Settings(SettingsPageFor(args.SettingsPage)),
        LaunchCommand.Uninstall => Uninstall(),
        LaunchCommand.BackgroundUpdate => BackgroundUpdate(),
        LaunchCommand.Welcome => Welcome(),
        LaunchCommand.EffectsTest => Vizstrap.Effects.EffectsSelfTest.Run(DepthModel.PathIn(App.Paths.Effects)) ? 0 : 1,
        LaunchCommand.Reinstall => App.IsInstalledCopy ? StartReinstall() : Install(fresh: true),
        _ when !App.IsInstalledCopy => Install(),
        _ => Menu(),
    };

    private static int Play(string? robloxUri, bool noLaunch = false)
    {
        SingletonGuard? guard = null;

        if (!noLaunch && !PrepareLaunch(out guard))
            return 0;

        // taken on this (the main) thread, which lives as long as the process
        using (guard)
        {
            var viewModel = new LoadingViewModel(robloxUri, noLaunch, LoadingAppearance.FromSettings(App.Settings.Value));
            LoadingWindows.Create(viewModel).ShowDialog();

            if (viewModel.Error is not null)
            {
                MessageWindow.ShowError(ErrorMessages.For(viewModel.Error), viewModel.Error);
                return 1;
            }

            if (viewModel.Launched is { } launched)
            {
                using var effects = StartEffects(launched.ProcessId);

                // Bloxstrap's watcher: activity tracking, Discord, tray icon and integrations, until Roblox exits
                Watcher.Run(launched);

                // the guard and the effects have to outlive this Roblox even when there's nothing to watch
                if (guard is not null || effects is not null)
                    RobloxInstances.WaitForExit(launched.ProcessId);
            }

            return 0;
        }
    }

    /// <summary>The picture effects over this Roblox when they're on; the game goes on without them if they can't start.</summary>
    private static Vizstrap.Effects.EffectsHost? StartEffects(int robloxProcessId)
    {
        var settings = App.Settings.Value.Effects;

        if (!settings.Enabled)
            return null;

        try
        {
            // the model goes along even when the depth AI is off: it can be switched on while playing
            string? model = DepthModel.IsReady(App.Paths.Effects) ? DepthModel.PathIn(App.Paths.Effects) : null;
            var options = EffectsLink.Options() with { CustomEffects = [.. App.EnabledPackages().SelectMany(package => package.Effects)] };
            return Vizstrap.Effects.EffectsHost.Start(robloxProcessId, settings, model, options);
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            return null;
        }
    }

    /// <summary>
    /// Deals with Robloxes already open. With several allowed, takes Roblox's singleton first: the tray
    /// app is closed quietly (a launch closes it anyway), open games only when the player agrees. Otherwise
    /// asks before a launch replaces the game being played. False when the player cancelled.
    /// </summary>
    private static bool PrepareLaunch(out SingletonGuard? guard)
    {
        guard = null;
        var settings = App.Settings.Value;
        var running = RobloxInstances.List();
        var games = running.Where(roblox => !roblox.IsTray).ToList();

        if (settings.MultiInstance)
        {
            if (SingletonGuard.IsHeldByRoblox())
            {
                Log.Info(LogSource, $"Roblox holds its singleton ({games.Count} open, {running.Count - games.Count} in the tray)");

                if (games.Count > 0 &&
                    !MessageWindow.Confirm(Strings.Launch_RunningTitle, Strings.Launch_CloseForMultiInstance, Strings.Launch_CloseAndLaunch, Strings.Common_Cancel))
                    return false;

                RobloxInstances.Close(running.Select(roblox => roblox.ProcessId));
            }

            guard = SingletonGuard.TryTake();

            if (guard is null)
                Log.Warn(LogSource, "Couldn't take Roblox's singleton; launching as a single Roblox");

            return true;
        }

        if (settings.ConfirmLaunches && games.Count > 0)
            return MessageWindow.Confirm(Strings.Launch_RunningTitle, Strings.Launch_Running, Strings.Launch_Continue, Strings.Common_Cancel);

        return true;
    }

    private static int Menu()
    {
        var window = new MenuWindow();
        window.ShowDialog();

        Log.Info(LogSource, $"Menu choice: {window.Choice}");

        return window.Choice switch
        {
            MenuChoice.Launch => Play(robloxUri: null),
            MenuChoice.Settings => Settings(),
            MenuChoice.About => Settings(typeof(AboutPage)),
            _ => 0,
        };
    }

    private static int Settings(Type? startPage = null)
    {
        var viewModel = new SettingsViewModel();
        new SettingsWindow(viewModel, startPage).ShowDialog();

        if (viewModel.ReinstallRequested)
            return StartReinstall();

        return viewModel.UninstallRequested ? Uninstall() : 0;
    }

    /// <summary>Maps "-settings appearance" style names to pages; unknown names open the first page.</summary>
    private static Type? SettingsPageFor(string? name) => name?.ToLowerInvariant() switch
    {
        "integrations" => typeof(IntegrationsPage),
        "bootstrapper" => typeof(BootstrapperPage),
        "mods" => typeof(ModsPage),
        "shaders" or "effects" => typeof(ShadersPage),
        "engine" => typeof(EngineSettingsPage),
        "fastflags" => typeof(FastFlagEditorPage),
        "appearance" => typeof(AppearancePage),
        "shortcuts" => typeof(ShortcutsPage),
        "vizstrap" => typeof(VizstrapPage),
        "about" => typeof(AboutPage),
        _ => null,
    };

    private static int Install(bool fresh = false)
    {
        var viewModel = new InstallerViewModel(fresh);

        // changing the language rebuilds the window so every text is re-read in the new language
        do
        {
            viewModel.LanguageChangeRequested = false;
            new InstallerWindow(viewModel).ShowDialog();
        }
        while (viewModel.LanguageChangeRequested);

        // a first install carries on in the installed copy: it finds its folder, wherever it was installed
        if (viewModel.InstalledTo is { } installed)
        {
            Process.Start(new ProcessStartInfo(installed.InstalledExecutable, "-welcome") { UseShellExecute = false })?.Dispose();
            return 0;
        }

        if (viewModel.LaunchRobloxRequested)
            StartInstalledPlayer();

        return 0;
    }

    /// <summary>The first-run wizard, after a fresh install or with "-welcome". Its choices are saved however it's closed.</summary>
    private static int Welcome()
    {
        var viewModel = new WelcomeViewModel();
        new WelcomeWindow(viewModel).ShowDialog();

        try
        {
            viewModel.Save();
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
        }

        if (!viewModel.LaunchRobloxRequested)
            return 0;

        // the installer is the downloaded file, not the installed copy: Roblox's watcher belongs to the latter
        if (App.IsInstalledCopy)
            return Play(robloxUri: null);

        StartInstalledPlayer();
        return 0;
    }

    /// <summary>
    /// The installed copy can't overwrite itself, so a copy of it in the temp folder runs the installer
    /// ("-reinstall") while this process exits.
    /// </summary>
    private static int StartReinstall()
    {
        if (!App.IsSingleFileBuild)
        {
            MessageWindow.ShowError(Strings.Installer_DevBuild, null);
            return 1;
        }

        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "Vizstrap");
            string installer = Path.Combine(folder, "Vizstrap-reinstall.exe");
            Directory.CreateDirectory(folder);
            File.Copy(Environment.ProcessPath!, installer, overwrite: true);

            Log.Info(LogSource, $"Reinstalling through {installer}");
            Process.Start(new ProcessStartInfo(installer, "-reinstall") { UseShellExecute = false })?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
            return 1;
        }
    }

    private static void StartInstalledPlayer() =>
        Process.Start(new ProcessStartInfo(App.Paths.InstalledExecutable, "-player") { UseShellExecute = false })?.Dispose();

    /// <summary>
    /// Downloads the newer Roblox a launch put off, while the old one is played; switches to it at the
    /// end (under the same lock as launches) so the next launch starts it. No windows.
    /// </summary>
    private static int BackgroundUpdate()
    {
        using var mutex = new Mutex(initiallyOwned: true, BackgroundUpdates.MutexName, out bool owned);

        if (!owned)
        {
            Log.Info(LogSource, "A background update is already running");
            return 0;
        }

        try
        {
            // stay out of the way of the game being played
            using (var self = Process.GetCurrentProcess())
                self.PriorityClass = ProcessPriorityClass.BelowNormal;

            using var cancelRequest = new EventWaitHandle(false, EventResetMode.ManualReset, BackgroundUpdates.CancelEventName);
            using var cancellation = new CancellationTokenSource();

            var waiter = Task.Run(() =>
            {
                if (WaitHandle.WaitAny([cancelRequest, cancellation.Token.WaitHandle]) == 0)
                {
                    Log.Info(LogSource, "Background update asked to stop");
                    cancellation.Cancel();
                }
            });

            // off the UI thread: its dispatcher isn't running, so awaits must not come back to it
            int exitCode = Task.Run(() => RunBackgroundUpdateAsync(cancellation.Token)).GetAwaiter().GetResult();

            cancellation.Cancel();
            waiter.GetAwaiter().GetResult();
            return exitCode;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static async Task<int> RunBackgroundUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = new JsonStore<State>(App.Paths.StateFile);
            state.Load();

            var updater = new RobloxUpdater(App.Paths, state, new RobloxDeployment(App.Http),
                new PackageDownloader(App.Http, App.Paths.Downloads, RobloxPaths.Downloads));

            var installed = updater.GetInstalled();
            var latest = await updater.GetLatestVersionAsync(cancellationToken);

            if (installed is null || installed.VersionGuid == latest.VersionGuid)
            {
                Log.Info(LogSource, $"Nothing to update in the background (installed: {installed?.VersionGuid ?? "none"})");
                return 0;
            }

            Log.Info(LogSource, $"Background update to {latest.VersionGuid} ({latest.Version})");
            var prepared = await updater.PrepareAsync(latest.VersionGuid, isFreshInstall: false, null, cancellationToken);

            using (await InterProcessLock.AcquireAsync(LoadingViewModel.BootstrapperLockName, null, cancellationToken))
                updater.Commit(prepared);

            Log.Info(LogSource, $"Background update done: the next launch starts {latest.VersionGuid}");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Info(LogSource, "Background update stopped; a launch will finish the job");
            return 0;
        }
        catch (Exception ex)
        {
            // the next launch updates as usual, so this only costs that launch its head start
            Log.Error(LogSource, ex);
            return 1;
        }
    }

    private static int Uninstall()
    {
        string size = ByteSize.Format(DirectorySize(App.Paths.Base));

        if (!MessageWindow.Confirm(Strings.Uninstall_Question, string.Format(Strings.Uninstall_Description, size),
                Strings.Uninstall_Confirm, Strings.Common_Cancel, danger: true))
            return 0;

        // only a Roblox started by Vizstrap blocks removal; the official client may keep running
        while (ProcessPaths.FindRunningUnder("RobloxPlayerBeta", App.Paths.Versions).Count > 0)
        {
            if (!MessageWindow.Confirm(Strings.Uninstall_Question, Strings.Uninstall_RobloxRunning,
                    Strings.Common_Retry, Strings.Common_Cancel))
                return 0;
        }

        // a background update would keep writing into the folder being removed
        Task.Run(() => BackgroundUpdates.StopAsync(TimeSpan.FromSeconds(15))).GetAwaiter().GetResult();

        var installer = SelfInstaller.ForCurrentUser(App.Paths);

        Log.Info(LogSource, "Uninstalling");
        Log.Shutdown(); // the logs folder is about to be deleted

        installer.Uninstall();

        if (App.IsInstalledCopy)
            installer.ScheduleFolderRemoval();

        MessageWindow.ShowInfo(Strings.Uninstall_Done);
        return 0;
    }

    private static long DirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        return new DirectoryInfo(directory)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }
}
