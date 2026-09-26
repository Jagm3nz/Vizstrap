using System.Diagnostics;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Install;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Launch;

/// <param name="LogFile">Roblox's log for this run; null if it couldn't be identified in time.</param>
public sealed record LaunchedRoblox(int ProcessId, string ExecutablePath, string? LogFile);

public static class RobloxLauncher
{
    private const string LogSource = nameof(RobloxLauncher);

    /// <summary>
    /// Starts Roblox and waits (up to <paramref name="timeout"/>) until its log file shows up, which
    /// means its window is on the way.
    /// </summary>
    public static async Task<LaunchedRoblox> LaunchAsync(
        InstalledRoblox roblox, string? robloxUri, string robloxLogsDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(robloxLogsDirectory);

        var startInfo = new ProcessStartInfo(roblox.ExecutablePath)
        {
            WorkingDirectory = roblox.Directory,
            UseShellExecute = false,
        };

        if (robloxUri is not null)
            startInfo.ArgumentList.Add(robloxUri);

        var started = DateTime.UtcNow;

        // don't hold on to the process handle: Roblox's anti-tamper dislikes long-lived open handles
        int processId;

        using (var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Roblox did not start."))
            processId = process.Id;

        Log.Info(LogSource, $"Started Roblox {roblox.VersionGuid} (PID {processId})");

        string? logFile = await RobloxLogLocator.FindAsync(robloxLogsDirectory, roblox.Directory, started, timeout, cancellationToken);

        if (logFile is null)
            Log.Warn(LogSource, "Roblox did not create a log file in time");
        else
            Log.Info(LogSource, $"Roblox log: {logFile}");

        return new LaunchedRoblox(processId, roblox.ExecutablePath, logFile);
    }
}
