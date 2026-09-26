using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Packages;
using Vizstrap.Core.Storage;

namespace Vizstrap.Integrations;

/// <summary>
/// Mod packages' programs (any language), started with Roblox and stopped with it. Each gets events as
/// JSON lines on stdin and sends commands as JSON lines on stdout (the protocol is in the package
/// template's guide, Packages.TemplateTexts). Commands are handled on the UI thread. What the player set on the
/// package's tabs comes with "started", and again as "settings" when it's changed while playing.
/// </summary>
internal sealed class PluginHost : IDisposable
{
    private const string LogSource = nameof(PluginHost);

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SettingsCheck = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly List<(InstalledPackage Package, Process Process)> _running = [];
    private readonly Action<string, string> _notify;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    private readonly Dictionary<string, string> _sentSettings = [];
    private System.Windows.Threading.DispatcherTimer? _settingsTimer;
    private DateTime _settingsStamp;

    private PluginHost(Action<string, string> notify) => _notify = notify;

    public int Count => _running.Count;

    /// <param name="notify">Shows a notification (title, text) from the tray icon, when there is one.</param>
    public static PluginHost Start(IEnumerable<InstalledPackage> packages, int robloxProcessId, Action<string, string> notify)
    {
        var host = new PluginHost(notify);

        foreach (var package in packages.Where(package => package.HasPlugin))
            host.StartOne(package);

        var saved = App.Settings.Value.PackageValues;

        foreach (var (package, process) in host._running)
        {
            var values = SettingsOf(package, saved);
            host._sentSettings[package.Id] = JsonSerializer.Serialize(values, Json);
            SendTo(package, process, new { Event = "started", Vizstrap = App.Version, RobloxProcessId = robloxProcessId, Settings = values });
        }

        host.WatchSettings();
        return host;
    }

    /// <summary>A package's settings as its program gets them: its tabs' inputs, the player's values or the defaults.</summary>
    private static Dictionary<string, object> SettingsOf(InstalledPackage package, IReadOnlyDictionary<string, Dictionary<string, string>> saved) =>
        PackagePages.Values(package.Pages("en"), saved.GetValueOrDefault(package.Id));

    /// <summary>Settings saved while playing reach the programs whose values changed.</summary>
    private void WatchSettings()
    {
        _settingsStamp = SettingsStamp();
        _settingsTimer = new System.Windows.Threading.DispatcherTimer { Interval = SettingsCheck };
        _settingsTimer.Tick += (_, _) =>
        {
            var stamp = SettingsStamp();

            if (stamp == _settingsStamp)
                return;

            _settingsStamp = stamp;
            var store = new JsonStore<Settings>(App.Paths.SettingsFile);
            store.Load();

            foreach (var (package, process) in _running)
            {
                var values = SettingsOf(package, store.Value.PackageValues);
                string json = JsonSerializer.Serialize(values, Json);

                if (_sentSettings.GetValueOrDefault(package.Id) == json)
                    continue;

                _sentSettings[package.Id] = json;
                SendTo(package, process, new { Event = "settings", Values = values });
            }
        };
        _settingsTimer.Start();
    }

    private static DateTime SettingsStamp() =>
        File.Exists(App.Paths.SettingsFile) ? File.GetLastWriteTimeUtc(App.Paths.SettingsFile) : DateTime.MinValue;

    public void GameJoined(GameSession session) => Send(new
    {
        Event = "gameJoined",
        session.PlaceId,
        session.UniverseId,
        session.JobId,
        ServerType = session.ServerType.ToString(),
        session.UserId,
    });

    public void GameLeft() => Send(new { Event = "gameLeft" });

    /// <summary>"stopping", then the programs get a moment to finish before they're ended.</summary>
    public void Dispose()
    {
        _settingsTimer?.Stop();
        Send(new { Event = "stopping" });
        var deadline = DateTime.UtcNow + StopTimeout;

        foreach (var (package, process) in _running)
        {
            try
            {
                var left = deadline - DateTime.UtcNow;

                if (!process.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero))
                {
                    process.Kill(entireProcessTree: true);
                    Log.Info(LogSource, $"{package.Id}'s plugin was ended");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // already gone
            }
            finally
            {
                process.Dispose();
            }
        }

        _running.Clear();
    }

    // ---- starting

    private void StartOne(InstalledPackage package)
    {
        var plugin = package.Manifest.Plugin!;

        try
        {
            var startInfo = new ProcessStartInfo(ProgramPath(package, plugin.Run))
            {
                WorkingDirectory = package.Directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (string argument in plugin.Args ?? [])
                startInfo.ArgumentList.Add(argument);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is { } line) OnCommand(package, line); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) Log.Info(LogSource, $"[{package.Id}] {line}"); };
            process.Start();
            process.StandardInput.AutoFlush = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _running.Add((package, process));
            Log.Info(LogSource, $"Started {package.Id}'s plugin: {plugin.Run} (PID {process.Id})");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or InvalidDataException)
        {
            Log.Warn(LogSource, $"{package.Id}'s plugin can't start: {ex.Message}");
        }
    }

    /// <summary>A program in the package (never outside it), or one found on the PATH like "python".</summary>
    internal static string ProgramPath(InstalledPackage package, string run)
    {
        if (!run.Contains('\\') && !run.Contains('/'))
        {
            string inPackage = Path.Combine(package.Directory, run);
            return File.Exists(inPackage) ? inPackage : run;
        }

        string full = Path.GetFullPath(Path.Combine(package.Directory, run));

        if (!full.StartsWith(Path.GetFullPath(package.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"\"run\" points outside the package: {run}");

        return full;
    }

    // ---- talking

    private void Send(object message)
    {
        foreach (var (package, process) in _running)
            SendTo(package, process, message);
    }

    private static void SendTo(InstalledPackage package, Process process, object message)
    {
        try
        {
            if (!process.HasExited)
                process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Json));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Log.Warn(LogSource, $"{package.Id}'s plugin stopped listening: {ex.Message}");
        }
    }

    private void OnCommand(InstalledPackage package, string line)
    {
        JsonObject? command;

        try
        {
            command = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            command = null;
        }

        try
        {
            Handle(package, line, command);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            Log.Info(LogSource, $"[{package.Id}] {line}");
        }
    }

    private void Handle(InstalledPackage package, string line, JsonObject? command)
    {
        string? name = command?["command"]?.GetValue<string>();

        switch (name)
        {
            case "notify":
                string title = command!["title"]?.GetValue<string>() ?? package.Manifest.Name;
                string text = command["text"]?.GetValue<string>() ?? "";
                _dispatcher.BeginInvoke(() => _notify(Trim(title, 64), Trim(text, 250)));
                break;

            case "setLook" when Enum.TryParse<EffectPreset>(command!["look"]?.GetValue<string>(), ignoreCase: true, out var look) && look != EffectPreset.Custom:
                EffectsLink.SaveLook(look);
                Log.Info(LogSource, $"[{package.Id}] look {look}");
                break;

            case "log":
                Log.Info(LogSource, $"[{package.Id}] {command!["text"]?.GetValue<string>()}");
                break;

            default:
                // anything else a program prints goes to the log
                Log.Info(LogSource, $"[{package.Id}] {line}");
                break;
        }
    }

    private static string Trim(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
