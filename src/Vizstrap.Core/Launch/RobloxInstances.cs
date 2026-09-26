using System.Diagnostics;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Platform;

namespace Vizstrap.Core.Launch;

/// <param name="IsTray">The Roblox app waiting in the notification area ("--launch-to-tray"), not a game or app window.</param>
public sealed record RunningRoblox(int ProcessId, bool IsTray);

/// <summary>Roblox players already running, from any folder (Vizstrap's or the official Roblox's).</summary>
public static class RobloxInstances
{
    public const string ProcessName = "RobloxPlayerBeta";

    private const string LogSource = nameof(RobloxInstances);

    public static IReadOnlyList<RunningRoblox> List()
    {
        var found = new List<RunningRoblox>();

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
                found.Add(new RunningRoblox(process.Id, IsTrayCommandLine(ProcessPaths.TryGetCommandLine(process.Id))));
        }

        return found;
    }

    public static bool IsTrayCommandLine(string? commandLine) =>
        commandLine?.Contains("--launch-to-tray", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Lists processes without opening handles: Roblox's anti-tamper dislikes held handles.</summary>
    public static bool IsRunning(int processId)
    {
        var processes = Process.GetProcesses();

        try
        {
            return processes.Any(process => process.Id == processId);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>Blocks until the process is gone, checking every couple of seconds.</summary>
    public static void WaitForExit(int processId)
    {
        while (IsRunning(processId))
            Thread.Sleep(TimeSpan.FromSeconds(2));
    }

    /// <summary>Ends the processes and waits (up to five seconds) until they are gone.</summary>
    public static void Close(IEnumerable<int> processIds)
    {
        var closing = processIds.ToList();

        foreach (int processId in closing)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill();
                Log.Info(LogSource, $"Closed Roblox (PID {processId})");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // already gone, or not ours to close
                Log.Warn(LogSource, $"Couldn't close Roblox (PID {processId}): {ex.Message}");
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (closing.Any(IsRunning) && DateTime.UtcNow < deadline)
            Thread.Sleep(200);
    }
}
