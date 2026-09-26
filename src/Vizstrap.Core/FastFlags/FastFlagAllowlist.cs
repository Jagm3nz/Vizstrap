namespace Vizstrap.Core.FastFlags;

public enum FlagCategory
{
    Geometry,
    Rendering,
    UserInterface,
}

/// <param name="DefaultValue">Roblox's value when the flag isn't set (from Roblox's PCDesktopClient settings).</param>
public sealed record AllowedFlag(string Name, FlagCategory Category, string DefaultValue);

/// <summary>
/// Since 29 September 2025 the Roblox client ignores every local Fast Flag that isn't on this list
/// (no penalty, the flag just does nothing). Source: Roblox's DevForum announcement "Allowlist for local
/// client configuration via Fast Flags"; unchanged as of Roblox staff's 26 January 2026 reply.
/// </summary>
public static class FastFlagAllowlist
{
    public const string AnnouncementUrl = "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";

    public static IReadOnlyList<AllowedFlag> All { get; } =
    [
        new("DFIntCSGLevelOfDetailSwitchingDistance", FlagCategory.Geometry, "250"),
        new("DFIntCSGLevelOfDetailSwitchingDistanceL12", FlagCategory.Geometry, "500"),
        new("DFIntCSGLevelOfDetailSwitchingDistanceL23", FlagCategory.Geometry, "750"),
        new("DFIntCSGLevelOfDetailSwitchingDistanceL34", FlagCategory.Geometry, "1000"),

        new("FFlagHandleAltEnterFullscreenManually", FlagCategory.Rendering, "True"),
        new("DFFlagTextureQualityOverrideEnabled", FlagCategory.Rendering, "False"),
        new("DFIntTextureQualityOverride", FlagCategory.Rendering, "3"),
        new("FIntDebugForceMSAASamples", FlagCategory.Rendering, "0"),
        new("DFFlagDisableDPIScale", FlagCategory.Rendering, "False"),
        new("FFlagDebugGraphicsPreferD3D11", FlagCategory.Rendering, "False"),
        new("FFlagDebugSkyGray", FlagCategory.Rendering, "False"),
        new("DFFlagDebugPauseVoxelizer", FlagCategory.Rendering, "False"),
        new("DFIntDebugFRMQualityLevelOverride", FlagCategory.Rendering, "0"),
        new("FIntFRMMaxGrassDistance", FlagCategory.Rendering, "290"),
        new("FIntFRMMinGrassDistance", FlagCategory.Rendering, "100"),
        new("FFlagDebugGraphicsPreferVulkan", FlagCategory.Rendering, "False"),
        new("FFlagDebugGraphicsPreferOpenGL", FlagCategory.Rendering, "False"),

        new("FIntGrassMovementReducedMotionFactor", FlagCategory.UserInterface, "5"),
    ];

    private static readonly Dictionary<string, AllowedFlag> ByName =
        All.ToDictionary(flag => flag.Name, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowed(string name) => ByName.ContainsKey(name);

    public static AllowedFlag? Find(string name) => ByName.GetValueOrDefault(name);
}
