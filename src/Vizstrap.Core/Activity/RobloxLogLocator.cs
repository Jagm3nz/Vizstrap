using System.Text;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Activity;

/// <summary>
/// Finds the log file of the Roblox Vizstrap just started. Taking the first new log isn't enough:
/// whenever a game closes, Roblox starts the official client in tray mode, which writes its own log.
/// </summary>
public static class RobloxLogLocator
{
    private const string LogSource = nameof(RobloxLogLocator);

    /// <summary>In the User-Agent Roblox logs at startup; marks the background tray process.</summary>
    internal const string TrayModeMarker = "AppState/TrayMode";

    /// <summary>How much of a log's start is read; the version folder shows up within its first lines.</summary>
    internal const int HeadLength = 64 * 1024;

    private static readonly TimeSpan CreationTolerance = TimeSpan.FromSeconds(2);

    internal enum Owner
    {
        /// <summary>Mentions the version folder Roblox was started from.</summary>
        Ours,

        /// <summary>The tray process's log.</summary>
        TrayProcess,

        /// <summary>Not recognisable (yet): the folder may still be about to be written.</summary>
        Unknown,
    }

    /// <summary>
    /// Waits for the log of the Roblox started at <paramref name="startedUtc"/> from
    /// <paramref name="versionDirectory"/>. If none mentions that folder in time, falls back to the
    /// first new log that isn't the tray process's; returns null when there is none.
    /// </summary>
    public static async Task<string?> FindAsync(
        string logsDirectory, string versionDirectory, DateTime startedUtc, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var trayLogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? fallback = null;

        while (true)
        {
            foreach (var file in NewLogs(logsDirectory, startedUtc))
            {
                if (trayLogs.Contains(file.FullName))
                    continue;

                switch (Inspect(file.FullName, versionDirectory))
                {
                    case Owner.Ours:
                        return file.FullName;

                    case Owner.TrayProcess:
                        Log.Info(LogSource, $"Skipping the tray process's log {file.Name}");
                        trayLogs.Add(file.FullName);
                        break;

                    default:
                        fallback ??= file.FullName;
                        break;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                if (fallback is not null)
                    Log.Warn(LogSource, $"No log mentions {versionDirectory}; using {Path.GetFileName(fallback)}");

                return fallback;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static IEnumerable<FileInfo> NewLogs(string logsDirectory, DateTime startedUtc)
    {
        if (!Directory.Exists(logsDirectory))
            return [];

        // Studio writes its logs into the same folder
        return new DirectoryInfo(logsDirectory)
            .EnumerateFiles("*.log")
            .Where(file => file.Name.Contains("Player", StringComparison.OrdinalIgnoreCase))
            .Where(file => file.CreationTimeUtc >= startedUtc - CreationTolerance)
            .OrderBy(file => file.CreationTimeUtc)
            .ToList();
    }

    internal static Owner Inspect(string path, string versionDirectory)
    {
        string head;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[HeadLength];
            int length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            head = Encoding.UTF8.GetString(buffer, 0, length);
        }
        catch (IOException)
        {
            return Owner.Unknown;
        }

        if (head.Contains(TrayModeMarker, StringComparison.Ordinal))
            return Owner.TrayProcess;

        string folder = Path.TrimEndingDirectorySeparator(versionDirectory) + Path.DirectorySeparatorChar;

        return head.Contains(folder, StringComparison.OrdinalIgnoreCase) ? Owner.Ours : Owner.Unknown;
    }
}
