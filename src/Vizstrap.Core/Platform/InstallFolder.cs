using Microsoft.Win32;

namespace Vizstrap.Core.Platform;

public enum InstallFolderProblem
{
    None,

    /// <summary>The folder has files that aren't Vizstrap's: uninstalling would delete them.</summary>
    NotEmpty,

    /// <summary>Folders can't be made there without administrator rights (Program Files, Windows…).</summary>
    NotWritable,

    /// <summary>Less free space than Vizstrap and Roblox need.</summary>
    LowSpace,
}

/// <summary>
/// The folder Vizstrap installs into, picked by the player on a first install. Everything lives there
/// (Vizstrap itself, Roblox, mods, settings), and the uninstaller deletes it whole, so it's always a folder of
/// its own: picking "D:\Games" installs into "D:\Games\Vizstrap".
/// </summary>
public static class InstallFolder
{
    public const string Name = "Vizstrap";

    /// <summary>Vizstrap, Roblox (about 400 MB unpacked), its download cache and room for an update.</summary>
    public const long RequiredBytes = 1024L * 1024 * 1024;

    /// <summary>The folder's own name is "Vizstrap": it's used as it is; any other gets a "Vizstrap" folder inside.</summary>
    public static string FromPicked(string picked)
    {
        string full = Path.GetFullPath(picked);
        string trimmed = Path.TrimEndingDirectorySeparator(full);

        return string.Equals(Path.GetFileName(trimmed), Name, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(full, Name);
    }

    /// <summary>What stands in the way of installing into <paramref name="folder"/>, if anything.</summary>
    public static InstallFolderProblem Check(string folder)
    {
        if (!IsFreeForVizstrap(folder))
            return InstallFolderProblem.NotEmpty;

        if (!CanCreateIn(folder))
            return InstallFolderProblem.NotWritable;

        if (FreeSpace(folder) is { } free && free < RequiredBytes)
            return InstallFolderProblem.LowSpace;

        return InstallFolderProblem.None;
    }

    /// <summary>Free bytes on the folder's drive, or null when the drive can't be read.</summary>
    public static long? FreeSpace(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The folder of the installed Vizstrap, from its "Apps and features" entry, while Vizstrap.exe is still
    /// there; null when Vizstrap isn't installed (or the entry can't be read).
    /// </summary>
    public static string? Installed(RegistryKey currentUser)
    {
        try
        {
            using var key = currentUser.OpenSubKey(SelfInstaller.UninstallKeyPath);

            return key?.GetValue("InstallLocation") is string { Length: > 0 } location
                   && File.Exists(Path.Combine(location, VizstrapPaths.ExecutableName))
                ? Path.GetFullPath(location)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Missing, empty, or already Vizstrap's.</summary>
    private static bool IsFreeForVizstrap(string folder) =>
        !Directory.Exists(folder)
        || !Directory.EnumerateFileSystemEntries(folder).Any()
        || File.Exists(Path.Combine(folder, VizstrapPaths.ExecutableName))
        || File.Exists(Path.Combine(folder, "Settings.json"));

    /// <summary>Tries a throwaway folder in the nearest folder that exists: what installing will need.</summary>
    private static bool CanCreateIn(string folder)
    {
        string? existing = Path.GetFullPath(folder);

        while (existing is not null && !Directory.Exists(existing))
            existing = Path.GetDirectoryName(existing);

        if (existing is null)
            return false;

        string probe = Path.Combine(existing, $".vizstrap-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(probe);
            File.WriteAllText(Path.Combine(probe, "probe"), "");
            Directory.Delete(probe, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
