using Microsoft.Win32;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Platform;

/// <summary>
/// Windows keeps an app's compatibility settings (DPI scaling, fullscreen optimisations…) per executable
/// path. Every Roblox update lives in a new folder, so the settings are carried over to the new path.
/// </summary>
public static class CompatibilityFlags
{
    public const string LayersKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    public static void Migrate(string oldExecutable, string newExecutable, RegistryKey currentUser)
    {
        if (string.Equals(oldExecutable, newExecutable, StringComparison.OrdinalIgnoreCase))
            return;

        using var key = currentUser.OpenSubKey(LayersKeyPath, writable: true);

        if (key?.GetValue(oldExecutable) is not string flags)
            return;

        key.SetValue(newExecutable, flags);
        key.DeleteValue(oldExecutable, throwOnMissingValue: false);

        Log.Info(nameof(CompatibilityFlags), $"Moved compatibility settings \"{flags}\" to {newExecutable}");
    }
}
