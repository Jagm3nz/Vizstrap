namespace Vizstrap.Core;

/// <summary>
/// Layout of Vizstrap's data directory. The base directory is injectable so tests can use a temp folder.
/// </summary>
public sealed class VizstrapPaths
{
    public const string ExecutableName = "Vizstrap.exe";

    public VizstrapPaths(string baseDirectory)
    {
        Base = Path.GetFullPath(baseDirectory);
    }

    /// <summary>Where Vizstrap installs unless the player picks another folder.</summary>
    public static string DefaultBase { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vizstrap");

    /// <summary>The installed Vizstrap's folder (wherever the player installed it), else the default one.</summary>
    public static VizstrapPaths Default { get; } = new(Platform.InstallFolder.Installed(Microsoft.Win32.Registry.CurrentUser) ?? DefaultBase);

    public string Base { get; }

    public string Versions => Path.Combine(Base, "Versions");

    public string Downloads => Path.Combine(Base, "Downloads");

    public string Logs => Path.Combine(Base, "Logs");

    /// <summary>Mods: files here are copied over the Roblox folder at the same relative path.</summary>
    public string Modifications => Path.Combine(Base, "Modifications");

    /// <summary>The font that replaces every in-game font family (see <see cref="Mods.ModApplier"/>).</summary>
    public string CustomFont => Path.Combine(Modifications, "content", "fonts", "CustomFont.ttf");

    public string SettingsFile => Path.Combine(Base, "Settings.json");

    /// <summary>Loading window themes in Bloxstrap's XML format, one folder each (see Appearance.XmlThemes).</summary>
    public string CustomThemes => Path.Combine(Base, "CustomThemes");

    /// <summary>The built-in themes written out in the current accent, to load from (see Appearance.BuiltinThemes).</summary>
    public string BuiltinThemes => Path.Combine(Base, "BuiltinThemes");

    /// <summary>The picture chosen for the "Custom" loading style, copied here so it outlives the original.</summary>
    public string LoadingBackgrounds => Path.Combine(Base, "LoadingBackground");

    /// <summary>Installed mod packages, one folder each (see Packages.PackageStore).</summary>
    public string Packages => Path.Combine(Base, "Packages");

    /// <summary>Play time per game, from Roblox's logs (see Activity.PlaytimeStore).</summary>
    public string Playtime => Path.Combine(Base, "Playtime.json");

    /// <summary>The picture effects' AI model (see Effects.DepthModel).</summary>
    public string Effects => Path.Combine(Base, "Effects");

    /// <summary>Bloxstrap's theme folder, offered for import.</summary>
    public static string BloxstrapCustomThemes { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloxstrap", "CustomThemes");

    public string StateFile => Path.Combine(Base, "State.json");

    public string InstalledExecutable => Path.Combine(Base, ExecutableName);

    public string VersionDirectory(string versionGuid) => Path.Combine(Versions, versionGuid);

    public bool IsInstalledExecutable(string? processPath) =>
        processPath is not null &&
        string.Equals(Path.GetFullPath(processPath), InstalledExecutable, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Folders that belong to the official Roblox client.
/// </summary>
public static class RobloxPaths
{
    public static string Base { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    /// <summary>The official bootstrapper's package cache (files are named by MD5).</summary>
    public static string Downloads => Path.Combine(Base, "Downloads");

    public static string Logs => Path.Combine(Base, "logs");
}
