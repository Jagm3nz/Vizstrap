using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Discord;
using Vizstrap.Core.Integrations;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Storage;
using Vizstrap.Localization;
using Vizstrap.ViewModels;
using Vizstrap.Views;

namespace Vizstrap.Integrations;

/// <summary>
/// Stays next to the Roblox that Vizstrap started, like Bloxstrap's watcher: follows its log,
/// shows the game on Discord, keeps a tray icon with a menu and closes custom integrations with it.
/// Runs in this process, on the UI thread, until that Roblox exits.
/// </summary>
internal sealed class Watcher
{
    private const string LogSource = nameof(Watcher);

    private readonly LaunchedRoblox _roblox;
    private readonly Settings _settings;
    private readonly string _discordPipePrefix;
    private readonly IReadOnlyList<int> _autoClose;
    private readonly ActivityTracker? _tracker;
    private readonly RobloxWebApi _api = new(App.Http);
    private readonly ServerLocator _locator = new(App.Http);
    private readonly CancellationTokenSource _stop = new();
    private readonly DispatcherFrame _frame = new();

    private DiscordIpcClient? _discord;
    private RichPresenceController? _presence;
    private TrayMenuWindow? _tray;
    private ServerInfoWindow? _serverInfo;
    private GameHistoryWindow? _gameHistory;
    private PluginHost? _plugins;
    private Task _tailing = Task.CompletedTask;

    private Watcher(LaunchedRoblox roblox, Settings settings, IReadOnlyList<int> autoClose, string discordPipePrefix = "discord-ipc-")
    {
        _roblox = roblox;
        _settings = settings;
        _autoClose = autoClose;
        _discordPipePrefix = discordPipePrefix;

        if (settings.EnableActivityTracking && roblox.LogFile is not null)
            _tracker = new ActivityTracker();
    }

    public string? LogFile => _roblox.LogFile;

    public bool HasRichPresence => _presence is not null;

    /// <summary>Like Bloxstrap, there's no history when leaving a game closes Roblox anyway.</summary>
    public bool HasGameHistory => _tracker is not null && !_settings.DisableDesktopApp;

    /// <summary>
    /// Starts the custom integrations, then watches Roblox until it exits if anything needs to:
    /// activity tracking (and what builds on it) or integrations to close with Roblox.
    /// </summary>
    public static void Run(LaunchedRoblox roblox)
    {
        var settings = App.Settings.Value;
        var autoClose = CustomIntegrations.StartAll(settings.CustomIntegrations);
        bool hasPlugins = App.EnabledPackages().Any(package => package.HasPlugin);

        if (!settings.EnableActivityTracking && autoClose.Count == 0 && !hasPlugins)
            return;

        new Watcher(roblox, settings, autoClose).Watch();
    }

#if DEBUG
    /// <summary>
    /// Debug builds only: watches any process and log with every integration on, talking to a test
    /// Discord pipe instead of the real app ("-testwatcher &lt;pid&gt; &lt;log&gt; &lt;pipe prefix&gt;").
    /// </summary>
    public static int RunForTest(int processId, string logFile, string discordPipePrefix)
    {
        var settings = new Settings { ShowServerLocation = true, AllowActivityJoining = true, ShowAccountOnProfile = true };
        new Watcher(new LaunchedRoblox(processId, "notepad.exe", logFile), settings, [], discordPipePrefix).Watch();
        return 0;
    }
#endif

    private void Watch()
    {
        Log.Info(LogSource, $"Watching Roblox (PID {_roblox.ProcessId}, tracking: {_tracker is not null}, integrations to close: {_autoClose.Count})");

        if (_tracker is not null)
            StartTracking(_roblox.LogFile!);

        if (_settings.EnableActivityTracking)
        {
            _tray = new TrayMenuWindow(this);
            _tray.Show();
        }

        // mod packages' programs, told about the game as it goes
        var plugins = App.EnabledPackages().Where(package => package.HasPlugin).ToList();

        if (plugins.Count > 0)
            _plugins = PluginHost.Start(plugins, _roblox.ProcessId, (title, text) => _tray?.Notify(title, text, null));

        var exitCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        exitCheck.Tick += async (_, _) =>
        {
            if (IsRobloxRunning())
                return;

            exitCheck.Stop();
            await StopAsync();
        };
        exitCheck.Start();

        // a nested message loop, like a dialog without a window, until StopAsync ends it
        Dispatcher.PushFrame(_frame);
    }

    private void StartTracking(string logFile)
    {
        var tracker = _tracker!;

        if (_settings.UseDiscordRichPresence)
        {
            // only an own Discord application has the Vizstrap logos
            string? ownApplication = string.IsNullOrWhiteSpace(_settings.DiscordApplicationId) ? null : _settings.DiscordApplicationId;

            _discord = new DiscordIpcClient(ownApplication ?? DiscordIpcClient.ApplicationId, _discordPipePrefix);
            _discord.Start();

            _presence = new RichPresenceController(_api, _discord.SetActivity,
                new PresenceOptions(_settings.AllowActivityJoining, _settings.ShowAccountOnProfile, _settings.ShowVizstrapWatermark,
                    _settings.ShowPresenceInMenu, ownApplication is null ? null : GamePresence.LogoKey(_settings.Accent)),
                new PresenceTexts(Strings.Presence_ByCreator, Strings.Presence_PrivateServer, Strings.Presence_ReservedServer,
                    Strings.Presence_JoinServer, Strings.Presence_GamePage, Strings.Presence_PlayingAs, Strings.Presence_InMenu),
                robloxStarted: DateTime.UtcNow);

            _presence.OnRobloxStartedAsync();
        }

        tracker.GameJoined += (_, session) => OnGameJoined(session);
        tracker.GameLeft += (_, _) => OnGameLeft();
        tracker.AppClosed += (_, _) => OnAppClosed();
        tracker.RpcMessageReceived += (_, message) => _presence?.OnRpcMessageAsync(message);

        // lines are read in the background and handed to the tracker on the UI thread, which owns it
        var dispatcher = Dispatcher.CurrentDispatcher;
        _tailing = Task.Run(() => LogTailer.FollowAsync(logFile, lines => dispatcher.Invoke(() =>
        {
            foreach (string line in lines)
                tracker.ProcessLine(line);
        }), TimeSpan.FromMilliseconds(500), _stop.Token));
    }

    private void OnGameJoined(GameSession session)
    {
        _plugins?.GameJoined(session);
        _presence?.OnGameJoinedAsync(session);
        _tray?.OnGameJoined(session);

        if (_settings.ShowServerLocation)
            NotifyServerLocation(session);
    }

    private void OnGameLeft()
    {
        _plugins?.GameLeft();
        _presence?.OnGameLeftAsync();
        _tray?.OnGameLeft();
        _serverInfo?.Close();

        if (_gameHistory?.DataContext is GameHistoryViewModel history)
            _ = history.LoadAsync();
    }

    private void OnAppClosed()
    {
        if (!_settings.DisableDesktopApp)
            return;

        Log.Info(LogSource, "Back in Roblox's app, closing it (\"Don't exit to desktop app\")");

        try
        {
            using var process = Process.GetProcessById(_roblox.ProcessId);
            process.CloseMainWindow();
        }
        catch (ArgumentException)
        {
            // already gone
        }
    }

    /// <summary>Bloxstrap's "Connected to … server / Location: …" notification.</summary>
    private async void NotifyServerLocation(GameSession session)
    {
        string? location = session.HasPublicAddress ? await _locator.LocateAsync(session.MachineAddress) : null;

        // the player may have moved on while the location loaded
        if (_tracker?.Current != session || _tray is null)
            return;

        string title = session.ServerType switch
        {
            ServerType.Private => Strings.Notification_Private,
            ServerType.Reserved => Strings.Notification_Reserved,
            _ => Strings.Notification_Public,
        };

        Log.Info(LogSource, $"Notifying: {title}, {location ?? "location unknown"}");
        _tray.Notify(title, string.Format(Strings.Notification_Location, location ?? Strings.Server_NotAvailable), ShowServerInfo);
    }

    public void SetRichPresenceVisible(bool visible) => _presence?.SetVisibleAsync(visible);

    public void CopyInviteLink()
    {
        if (_tracker?.Current is { } session)
            Clipboard.SetText(session.InviteDeeplink());
    }

    public void ShowServerInfo()
    {
        if (_tracker?.Current is not { } session)
            return;

        if (_serverInfo is null)
        {
            _serverInfo = new ServerInfoWindow(session, _settings.ShowServerLocation ? _locator : null);
            _serverInfo.Closed += (_, _) => _serverInfo = null;
            _serverInfo.Show();
        }

        _serverInfo.Activate();
    }

    public void ShowGameHistory()
    {
        if (_tracker is null)
            return;

        if (_gameHistory is null)
        {
            _gameHistory = new GameHistoryWindow(new GameHistoryViewModel(() => _tracker.History, _api, Rejoin));
            _gameHistory.Closed += (_, _) => _gameHistory = null;
            _gameHistory.Show();
        }

        _gameHistory.Activate();
    }

    public void CloseRoblox()
    {
        if (!MessageWindow.Confirm(Strings.Tray_CloseRobloxQuestion, Strings.Tray_CloseRobloxMessage,
                Strings.Tray_CloseRoblox, Strings.Common_Cancel, danger: true))
            return;

        try
        {
            using var process = Process.GetProcessById(_roblox.ProcessId);
            Log.Info(LogSource, "Closing Roblox from the tray menu");
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn(LogSource, $"Couldn't close Roblox: {ex.Message}");
        }
    }

    public void OpenLog()
    {
        if (LogFile is not null && File.Exists(LogFile))
            Process.Start(new ProcessStartInfo(LogFile) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// Hands the link to the Roblox executable, which passes it on to the running Roblox (Bloxstrap
    /// does the same), so the game opens in the window that's already there.
    /// </summary>
    private void Rejoin(GameVisit visit)
    {
        Log.Info(LogSource, $"Rejoining {visit.RejoinTarget}");

        var startInfo = new ProcessStartInfo(_roblox.ExecutablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(_roblox.ExecutablePath) ?? "",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(visit.RejoinDeeplink);

        Process.Start(startInfo)?.Dispose();
    }

    /// <summary>Lists processes without opening handles: Roblox's anti-tamper dislikes held handles.</summary>
    private bool IsRobloxRunning()
    {
        var processes = Process.GetProcesses();

        try
        {
            return processes.Any(process => process.Id == _roblox.ProcessId);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>This run's stays go into the play time history, read whole from the log once Roblox is gone.</summary>
    private void RecordPlaytime()
    {
        if (_tracker is null || _roblox.LogFile is not { } logFile)
            return;

        try
        {
            int added = 0;
            PlaytimeStore.Update(App.Paths.Playtime, store => added = store.Add(PlaytimeLog.Read(logFile)));
            Log.Info(LogSource, $"Play time: {added} stays recorded");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogSource, $"Play time couldn't be recorded: {ex.Message}");
        }
    }

    private async Task StopAsync()
    {
        Log.Info(LogSource, "Roblox has exited");

        _stop.Cancel();

        try
        {
            await _tailing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Log.Warn(LogSource, $"Log reading ended with: {ex.Message}");
        }

        if (_discord is not null)
            await _discord.DisposeAsync();

        RecordPlaytime();
        CustomIntegrations.CloseAll(_autoClose);
        _plugins?.Dispose();

        _serverInfo?.Close();
        _gameHistory?.Close();
        _tray?.Close();

        _frame.Continue = false;
    }
}
