namespace Vizstrap.Core.Launch;

public enum LaunchCommand
{
    /// <summary>No command: installer or launch menu.</summary>
    None,
    Player,
    Settings,
    Uninstall,
    /// <summary>Download a newer Roblox for the next launch, without windows ("-backgroundupdate").</summary>
    BackgroundUpdate,
    /// <summary>The first-run wizard again ("-welcome").</summary>
    Welcome,
    /// <summary>The installer as on a first install, wizard included, even over an installed copy ("-reinstall").</summary>
    Reinstall,
    /// <summary>Check the picture effects on this computer, without windows ("-effectstest"; the log has the result).</summary>
    EffectsTest,
}

/// <param name="RobloxUri">A "roblox-player:" or "roblox:" link to pass to Roblox; null opens the Roblox app home.</param>
/// <param name="NoLaunch">Install or update Roblox but don't start it ("-nolaunch").</param>
/// <param name="SettingsPage">Settings page to open first ("-settings appearance").</param>
public sealed record LaunchArgs(LaunchCommand Command, string? RobloxUri = null, bool NoLaunch = false, string? SettingsPage = null)
{
    private static readonly string[] RobloxSchemes = ["roblox-player:", "roblox:"];

    public static LaunchArgs Parse(IReadOnlyList<string> args)
    {
        bool noLaunch = args.Any(arg => string.Equals(arg.Trim(), "-nolaunch", StringComparison.OrdinalIgnoreCase));

        return ParseCommand(args) with { NoLaunch = noLaunch };
    }

    private static LaunchArgs ParseCommand(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i].Trim();

            if (IsRobloxUri(arg))
                return new LaunchArgs(LaunchCommand.Player, arg);

            switch (arg.ToLowerInvariant())
            {
                case "-player":
                    string? uri = i + 1 < args.Count && IsRobloxUri(args[i + 1].Trim()) ? args[i + 1].Trim() : null;
                    return new LaunchArgs(LaunchCommand.Player, uri);

                case "-settings":
                    string? page = i + 1 < args.Count && !args[i + 1].StartsWith('-') ? args[i + 1].Trim() : null;
                    return new LaunchArgs(LaunchCommand.Settings, SettingsPage: page);

                case "-uninstall":
                    return new LaunchArgs(LaunchCommand.Uninstall);

                case "-backgroundupdate":
                    return new LaunchArgs(LaunchCommand.BackgroundUpdate);

                case "-welcome":
                    return new LaunchArgs(LaunchCommand.Welcome);

                case "-reinstall":
                    return new LaunchArgs(LaunchCommand.Reinstall);

                case "-effectstest":
                    return new LaunchArgs(LaunchCommand.EffectsTest);
            }
        }

        return new LaunchArgs(LaunchCommand.None);
    }

    private static bool IsRobloxUri(string value) =>
        RobloxSchemes.Any(scheme => value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
}
