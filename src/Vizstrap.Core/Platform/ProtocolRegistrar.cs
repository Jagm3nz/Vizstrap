using Microsoft.Win32;

namespace Vizstrap.Core.Platform;

/// <summary>
/// Owns the "roblox" and "roblox-player" URL protocols under HKCU\Software\Classes (which win over HKLM).
/// The registry root is injectable so tests can work on a throwaway key.
/// </summary>
public sealed class ProtocolRegistrar
{
    public static IReadOnlyList<string> PlayerProtocols { get; } = ["roblox", "roblox-player"];

    private readonly RegistryKey _classesRoot;

    public ProtocolRegistrar(RegistryKey classesRoot)
    {
        _classesRoot = classesRoot;
    }

    public static ProtocolRegistrar ForCurrentUser() => new(Registry.CurrentUser.CreateSubKey(@"Software\Classes"));

    public static string VizstrapCommand(string vizstrapExecutable) => $"\"{vizstrapExecutable}\" -player \"%1\"";

    public static string OfficialCommand(string robloxPlayerExecutable) => $"\"{robloxPlayerExecutable}\" %1";

    public void RegisterVizstrap(string vizstrapExecutable) =>
        Register(VizstrapCommand(vizstrapExecutable), vizstrapExecutable);

    /// <summary>Points the protocols at any handler command (restoring a previous launcher).</summary>
    public void Register(string command, string iconPath)
    {
        foreach (string protocol in PlayerProtocols)
        {
            using var key = _classesRoot.CreateSubKey(protocol);
            key.SetValue("", "URL: Roblox Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", iconPath);

            using var commandKey = key.CreateSubKey(@"shell\open\command");

            if (commandKey.GetValue("") as string != command)
                commandKey.SetValue("", command);
        }
    }

    public void Unregister()
    {
        foreach (string protocol in PlayerProtocols)
            _classesRoot.DeleteSubKeyTree(protocol, throwOnMissingSubKey: false);
    }

    public string? GetCommand(string protocol)
    {
        using var key = _classesRoot.OpenSubKey($@"{protocol}\shell\open\command");
        return key?.GetValue("") as string;
    }

    /// <summary>Extracts the executable from a handler command such as <c>"C:\x\app.exe" -player "%1"</c>.</summary>
    public static string? GetExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
