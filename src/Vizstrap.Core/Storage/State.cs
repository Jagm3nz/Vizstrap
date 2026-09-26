namespace Vizstrap.Core.Storage;

/// <summary>What Vizstrap has installed (State.json). Written only after an install fully succeeds.</summary>
public sealed class State
{
    /// <summary>Installed Roblox Player version, e.g. "version-2366ba214ec740ca".</summary>
    public string? PlayerVersion { get; set; }

    /// <summary>Package name → MD5 of the installed version; used to prune the download cache.</summary>
    public Dictionary<string, string> PackageHashes { get; set; } = new();

    /// <summary>Installed Roblox size (packed + unpacked) in kilobytes, shown in "Apps and features".</summary>
    public long PlayerSizeKb { get; set; }

    /// <summary>Set from settings to re-extract Roblox on the next launch.</summary>
    public bool ForceReinstall { get; set; }

    /// <summary>Mod files (relative paths) applied on the last launch; used to restore originals of removed mods.</summary>
    public List<string> ModManifest { get; set; } = new();
}
