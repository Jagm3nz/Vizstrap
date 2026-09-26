namespace Vizstrap.Core.Platform;

/// <summary>.lnk files through the Windows Script Host COM object.</summary>
public static class Shortcut
{
    public static void Create(string shortcutPath, string targetPath, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        dynamic shell = CreateShell();
        dynamic shortcut = shell.CreateShortcut(shortcutPath);

        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.Description = description;
        shortcut.IconLocation = $"{targetPath},0";
        shortcut.Save();
    }

    public static string? GetTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return null;

        dynamic shell = CreateShell();
        dynamic shortcut = shell.CreateShortcut(shortcutPath);

        return shortcut.TargetPath as string;
    }

    private static dynamic CreateShell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new PlatformNotSupportedException("WScript.Shell is not available.");
        return Activator.CreateInstance(type)!;
    }
}
