using System.Diagnostics;
using Microsoft.Win32;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Platform;

/// <summary>
/// Installs Vizstrap for the current user (no admin): copies the executable to its folder (%LocalAppData%\Vizstrap
/// unless the player picked another, see <see cref="InstallFolder"/>), adds shortcuts and an "Apps and features"
/// entry, whose InstallLocation is how Vizstrap finds its folder again, and takes over the Roblox links,
/// remembering which launcher had them so uninstalling can hand them back.
/// </summary>
public sealed class SelfInstaller
{
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Vizstrap";
    public const string OfficialPlayerUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player";
    public const string VizstrapKeyPath = @"Software\Vizstrap";

    private const string LogSource = nameof(SelfInstaller);
    private const string PreviousHandlerPrefix = "PreviousHandler_";

    private readonly VizstrapPaths _paths;
    private readonly ProtocolRegistrar _protocols;
    private readonly RegistryKey _currentUser;
    private readonly string _startMenuDirectory;
    private readonly string _desktopDirectory;

    public SelfInstaller(
        VizstrapPaths paths,
        ProtocolRegistrar protocols,
        RegistryKey currentUser,
        string startMenuDirectory,
        string desktopDirectory)
    {
        _paths = paths;
        _protocols = protocols;
        _currentUser = currentUser;
        _startMenuDirectory = startMenuDirectory;
        _desktopDirectory = desktopDirectory;
    }

    public static SelfInstaller ForCurrentUser(VizstrapPaths paths) => new(
        paths,
        ProtocolRegistrar.ForCurrentUser(),
        Registry.CurrentUser,
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public string StartMenuShortcut => Path.Combine(_startMenuDirectory, "Vizstrap.lnk");

    public string DesktopShortcut => Path.Combine(_desktopDirectory, "Vizstrap.lnk");

    public bool IsInstalled
    {
        get
        {
            using var key = _currentUser.OpenSubKey(UninstallKeyPath);
            return key is not null && File.Exists(_paths.InstalledExecutable);
        }
    }

    /// <summary>Product version of the installed executable, or null when not installed.</summary>
    public string? InstalledVersion => File.Exists(_paths.InstalledExecutable)
        ? FileVersionInfo.GetVersionInfo(_paths.InstalledExecutable).ProductVersion
        : null;

    public void Install(string sourceExecutable, string version, bool createDesktopShortcut, long robloxSizeKb, bool createStartMenuShortcut = true)
    {
        string executable = _paths.InstalledExecutable;

        Directory.CreateDirectory(_paths.Base);

        if (!_paths.IsInstalledExecutable(sourceExecutable))
            File.Copy(sourceExecutable, executable, overwrite: true);

        if (createStartMenuShortcut)
            Shortcut.Create(StartMenuShortcut, executable, "Vizstrap");

        if (createDesktopShortcut)
            Shortcut.Create(DesktopShortcut, executable, "Vizstrap");

        WriteUninstallEntry(version, robloxSizeKb);
        RememberPreviousHandlers();
        _protocols.RegisterVizstrap(executable);

        Log.Info(LogSource, $"Installed {version} to {_paths.Base}");
    }

    public bool HasStartMenuShortcut => PointsToVizstrap(StartMenuShortcut);

    public bool HasDesktopShortcut => PointsToVizstrap(DesktopShortcut);

    public void SetStartMenuShortcut(bool enabled) => SetShortcut(StartMenuShortcut, enabled);

    public void SetDesktopShortcut(bool enabled) => SetShortcut(DesktopShortcut, enabled);

    private void SetShortcut(string shortcutPath, bool enabled)
    {
        if (enabled)
            Shortcut.Create(shortcutPath, _paths.InstalledExecutable, "Vizstrap");
        else if (PointsToVizstrap(shortcutPath))
            File.Delete(shortcutPath);
    }

    private bool PointsToVizstrap(string shortcutPath) =>
        string.Equals(Shortcut.GetTarget(shortcutPath), _paths.InstalledExecutable, StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the "Apps and features" size in sync after Roblox updates.</summary>
    public void UpdateEstimatedSize(long robloxSizeKb)
    {
        using var key = _currentUser.OpenSubKey(UninstallKeyPath, writable: true);
        key?.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, EstimatedSizeKb(robloxSizeKb)), RegistryValueKind.DWord);
    }

    /// <summary>
    /// Removes everything Vizstrap created. When Vizstrap is running from the install folder the folder
    /// itself can't be deleted yet: call <see cref="ScheduleFolderRemoval"/> right before exiting.
    /// </summary>
    public void Uninstall()
    {
        RestoreProtocolHandlers();

        TryRun(() => SetStartMenuShortcut(false));
        TryRun(() => SetDesktopShortcut(false));

        TryRun(() => _currentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false));
        TryRun(() => _currentUser.DeleteSubKeyTree(VizstrapKeyPath, throwOnMissingSubKey: false));

        foreach (string directory in new[] { _paths.Versions, _paths.Downloads, _paths.Logs })
            TryRun(() => { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); });

        TryRun(() => File.Delete(_paths.SettingsFile));
        TryRun(() => File.Delete(_paths.StateFile));

        if (!_paths.IsInstalledExecutable(Environment.ProcessPath))
            TryRun(() => { if (Directory.Exists(_paths.Base)) Directory.Delete(_paths.Base, recursive: true); });
    }

    /// <summary>Deletes the install folder a few seconds after this process exits.</summary>
    public void ScheduleFolderRemoval()
    {
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            // ping is the classic delay that also works without a console for "timeout" to read from
            Arguments = $"/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"{_paths.Base}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        })?.Dispose();
    }

    /// <summary>The official Roblox Player executable, when the official bootstrapper installed one.</summary>
    public string? FindOfficialPlayer()
    {
        using var key = _currentUser.OpenSubKey(OfficialPlayerUninstallKeyPath);

        if (key?.GetValue("InstallLocation") is not string location)
            return null;

        string executable = Path.Combine(location, "RobloxPlayerBeta.exe");
        return File.Exists(executable) ? executable : null;
    }

    private void RememberPreviousHandlers()
    {
        using var key = _currentUser.CreateSubKey(VizstrapKeyPath);

        foreach (string protocol in ProtocolRegistrar.PlayerProtocols)
        {
            string? command = _protocols.GetCommand(protocol);

            // reinstalling must not remember Vizstrap as its own predecessor
            if (command is null || IsVizstrapCommand(command))
                continue;

            key.SetValue(PreviousHandlerPrefix + protocol, command);
        }
    }

    private void RestoreProtocolHandlers()
    {
        // someone else (e.g. the official bootstrapper) already took the links back
        if (!ProtocolRegistrar.PlayerProtocols.Any(protocol => IsVizstrapCommand(_protocols.GetCommand(protocol))))
            return;

        using var key = _currentUser.OpenSubKey(VizstrapKeyPath);
        string? previous = key?.GetValue(PreviousHandlerPrefix + "roblox-player") as string;
        string? previousExecutable = ProtocolRegistrar.GetExecutable(previous);

        if (previous is not null && previousExecutable is not null && File.Exists(previousExecutable))
        {
            Log.Info(LogSource, $"Handing Roblox links back to {previousExecutable}");
            _protocols.Register(previous, previousExecutable);
            return;
        }

        string? official = FindOfficialPlayer();

        if (official is not null)
        {
            Log.Info(LogSource, $"Handing Roblox links to the official player {official}");
            _protocols.Register(ProtocolRegistrar.OfficialCommand(official), official);
            return;
        }

        Log.Info(LogSource, "No other Roblox launcher found, removing the Roblox links");
        _protocols.Unregister();
    }

    private bool IsVizstrapCommand(string? command) =>
        string.Equals(ProtocolRegistrar.GetExecutable(command), _paths.InstalledExecutable, StringComparison.OrdinalIgnoreCase);

    private void WriteUninstallEntry(string version, long robloxSizeKb)
    {
        string executable = _paths.InstalledExecutable;

        using var key = _currentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", "Vizstrap");
        key.SetValue("DisplayVersion", version);
        key.SetValue("DisplayIcon", $"{executable},0");
        key.SetValue("Publisher", "Vizstrap");
        key.SetValue("InstallLocation", _paths.Base);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", $"\"{executable}\" -uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, EstimatedSizeKb(robloxSizeKb)), RegistryValueKind.DWord);
    }

    private long EstimatedSizeKb(long robloxSizeKb)
    {
        long exeKb = File.Exists(_paths.InstalledExecutable) ? new FileInfo(_paths.InstalledExecutable).Length / 1024 : 0;
        return exeKb + robloxSizeKb;
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn(LogSource, $"Cleanup step failed: {ex.Message}");
        }
    }
}
