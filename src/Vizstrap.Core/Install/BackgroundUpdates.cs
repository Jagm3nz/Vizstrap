using System.Diagnostics;
using System.Globalization;

namespace Vizstrap.Core.Install;

/// <summary>
/// Bloxstrap's background updates: when Roblox has a new version, launch the installed one right
/// away and download the new one while playing, for the next launch. Only for small steps, since
/// Roblox's servers keep accepting the previous version for a while but not older ones.
/// </summary>
public static class BackgroundUpdates
{
    /// <summary>Held by the background update process while it runs.</summary>
    public const string MutexName = "Vizstrap-BackgroundUpdate";

    /// <summary>Set to ask a running background update to stop (a launch that has to update now, uninstalling).</summary>
    public const string CancelEventName = "Vizstrap-BackgroundUpdate-Cancel";

    /// <summary>As in Bloxstrap: the new version is downloaded next to the old one, so room is needed for both.</summary>
    public const long MinimumFreeSpace = 5_000_000_000;

    /// <summary>
    /// Whether the update to <paramref name="latestVersion"/> ("0.741.0.7410123") can wait:
    /// same or next Roblox release (e.g. 0.740 → 0.741), nothing forces a reinstall, and there's room.
    /// </summary>
    public static bool CanDefer(Version? installed, string latestVersion, long freeSpace, bool forceReinstall, out string reason)
    {
        reason = forceReinstall ? "a reinstall is due" :
            freeSpace < MinimumFreeSpace ? $"only {freeSpace} bytes free" :
            installed is null ? "the installed version is unknown" :
            !TryParseRelease(latestVersion, out var latest) ? $"the latest version \"{latestVersion}\" is unreadable" :
            latest.Major != installed.Major || latest.Minor - installed.Minor is < 0 or > 1 ? $"{installed.Major}.{installed.Minor} → {latest.Major}.{latest.Minor} is too big a step" :
            "";

        return reason.Length == 0;
    }

    /// <summary>The Roblox release of an installed executable (e.g. 0.740), from its file version; null if it has none.</summary>
    public static Version? ReadRelease(string executable)
    {
        if (!File.Exists(executable))
            return null;

        var info = FileVersionInfo.GetVersionInfo(executable);
        return info.FileMajorPart == 0 && info.FileMinorPart == 0 ? null : new Version(info.FileMajorPart, info.FileMinorPart);
    }

    internal static bool TryParseRelease(string version, out Version release)
    {
        var parts = version.Split('.');
        release = new Version();

        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
            return false;

        release = new Version(major, minor);
        return true;
    }

    /// <summary>Whether a background update is running (in any process).</summary>
    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var mutex))
            return false;

        mutex.Dispose();
        return true;
    }

    /// <summary>Asks a running background update to stop and waits for it (up to <paramref name="timeout"/>).</summary>
    public static async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (EventWaitHandle.TryOpenExisting(CancelEventName, out var cancel))
        {
            using (cancel)
                cancel.Set();
        }

        var deadline = DateTime.UtcNow + timeout;

        while (IsRunning())
        {
            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(100, cancellationToken);
        }

        return true;
    }
}
