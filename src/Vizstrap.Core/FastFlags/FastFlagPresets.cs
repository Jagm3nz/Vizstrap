namespace Vizstrap.Core.FastFlags;

public enum MsaaMode
{
    Automatic,
    X1,
    X2,
    X4,
}

public enum TextureQuality
{
    Automatic,
    Level0,
    Level1,
    Level2,
    Level3,
}

public enum GraphicsApi
{
    Automatic,
    Direct3D11,
    Vulkan,
    OpenGL,
}

/// <summary>How far away unions and meshes switch to simpler versions of themselves.</summary>
public enum GeometryDetail
{
    Automatic,
    /// <summary>Always the simplest version (all switching distances 0): faster.</summary>
    Low,
    /// <summary>Detailed versions kept twice as far as Roblox's defaults.</summary>
    High,
}

/// <summary>
/// The settings on the "Engine settings" page, each expressed through allowlisted flags. They read and
/// write one flag dictionary, so presets and the flag editor always agree. Turning a preset off removes
/// its flags instead of writing Roblox's default, so Roblox keeps full control of those values.
/// </summary>
public static class FastFlagPresets
{
    private const string Msaa = "FIntDebugForceMSAASamples";
    private const string DisableDpiScale = "DFFlagDisableDPIScale";
    private const string TextureOverride = "DFFlagTextureQualityOverrideEnabled";
    private const string TextureLevel = "DFIntTextureQualityOverride";
    private const string ManualAltEnter = "FFlagHandleAltEnterFullscreenManually";
    private const string PreferD3D11 = "FFlagDebugGraphicsPreferD3D11";
    private const string PreferVulkan = "FFlagDebugGraphicsPreferVulkan";
    private const string PreferOpenGL = "FFlagDebugGraphicsPreferOpenGL";
    private const string GraySky = "FFlagDebugSkyGray";
    private const string QualityLevel = "DFIntDebugFRMQualityLevelOverride";
    private const string MinGrass = "FIntFRMMinGrassDistance";
    private const string MaxGrass = "FIntFRMMaxGrassDistance";
    private const string PauseVoxelizer = "DFFlagDebugPauseVoxelizer";
    private const string GrassMotion = "FIntGrassMovementReducedMotionFactor";

    private static readonly string[] LodFlags =
    [
        "DFIntCSGLevelOfDetailSwitchingDistance",
        "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23",
        "DFIntCSGLevelOfDetailSwitchingDistanceL34",
    ];

    private static readonly string[] LowLod = ["0", "0", "0", "0"];

    // twice Roblox's 250 / 500 / 750 / 1000
    private static readonly string[] HighLod = ["500", "1000", "1500", "2000"];

    /// <summary>Roblox's highest graphics quality level.</summary>
    public const int MaxQualityLevel = 21;

    // ---- MSAA

    public static MsaaMode GetMsaa(IDictionary<string, string> flags) => Get(flags, Msaa) switch
    {
        "1" => MsaaMode.X1,
        "2" => MsaaMode.X2,
        "4" => MsaaMode.X4,
        _ => MsaaMode.Automatic,
    };

    public static void SetMsaa(IDictionary<string, string> flags, MsaaMode mode) => Set(flags, Msaa, mode switch
    {
        MsaaMode.X1 => "1",
        MsaaMode.X2 => "2",
        MsaaMode.X4 => "4",
        _ => null,
    });

    // ---- DPI scaling

    public static bool GetDisableDpiScale(IDictionary<string, string> flags) => IsTrue(flags, DisableDpiScale);

    public static void SetDisableDpiScale(IDictionary<string, string> flags, bool enabled) =>
        Set(flags, DisableDpiScale, enabled ? "True" : null);

    // ---- texture quality

    public static TextureQuality GetTextureQuality(IDictionary<string, string> flags)
    {
        if (!IsTrue(flags, TextureOverride))
            return TextureQuality.Automatic;

        return Get(flags, TextureLevel) switch
        {
            "0" => TextureQuality.Level0,
            "1" => TextureQuality.Level1,
            "2" => TextureQuality.Level2,
            "3" => TextureQuality.Level3,
            _ => TextureQuality.Automatic,
        };
    }

    public static void SetTextureQuality(IDictionary<string, string> flags, TextureQuality quality)
    {
        if (quality == TextureQuality.Automatic)
        {
            Set(flags, TextureOverride, null);
            Set(flags, TextureLevel, null);
            return;
        }

        Set(flags, TextureOverride, "True");
        Set(flags, TextureLevel, ((int)quality - 1).ToString());
    }

    // ---- exclusive fullscreen

    /// <summary>Alt+Enter switches to Direct3D exclusive fullscreen instead of Roblox's own borderless mode.</summary>
    public static bool GetExclusiveFullscreen(IDictionary<string, string> flags) => IsFalse(flags, ManualAltEnter);

    public static void SetExclusiveFullscreen(IDictionary<string, string> flags, bool enabled) =>
        Set(flags, ManualAltEnter, enabled ? "False" : null);

    // ---- graphics API

    public static GraphicsApi GetGraphicsApi(IDictionary<string, string> flags) =>
        IsTrue(flags, PreferD3D11) ? GraphicsApi.Direct3D11 :
        IsTrue(flags, PreferVulkan) ? GraphicsApi.Vulkan :
        IsTrue(flags, PreferOpenGL) ? GraphicsApi.OpenGL :
        GraphicsApi.Automatic;

    public static void SetGraphicsApi(IDictionary<string, string> flags, GraphicsApi api)
    {
        Set(flags, PreferD3D11, api == GraphicsApi.Direct3D11 ? "True" : null);
        Set(flags, PreferVulkan, api == GraphicsApi.Vulkan ? "True" : null);
        Set(flags, PreferOpenGL, api == GraphicsApi.OpenGL ? "True" : null);
    }

    // ---- graphics quality level

    /// <summary>The forced graphics quality level (1–21), or null when Roblox decides.</summary>
    public static int? GetQualityLevel(IDictionary<string, string> flags) =>
        int.TryParse(Get(flags, QualityLevel), out int level) && level is >= 1 and <= MaxQualityLevel ? level : null;

    public static void SetQualityLevel(IDictionary<string, string> flags, int? level) =>
        Set(flags, QualityLevel, level is >= 1 and <= MaxQualityLevel ? level.Value.ToString() : null);

    // ---- gray sky

    public static bool GetGraySky(IDictionary<string, string> flags) => IsTrue(flags, GraySky);

    public static void SetGraySky(IDictionary<string, string> flags, bool enabled) => Set(flags, GraySky, enabled ? "True" : null);

    // ---- grass

    /// <summary>Terrain grass isn't drawn at all (both grass distances 0).</summary>
    public static bool GetNoGrass(IDictionary<string, string> flags) =>
        Get(flags, MinGrass) == "0" && Get(flags, MaxGrass) == "0";

    public static void SetNoGrass(IDictionary<string, string> flags, bool enabled)
    {
        Set(flags, MinGrass, enabled ? "0" : null);
        Set(flags, MaxGrass, enabled ? "0" : null);
    }

    // ---- geometry detail

    public static GeometryDetail GetGeometryDetail(IDictionary<string, string> flags)
    {
        var values = LodFlags.Select(name => Get(flags, name)).ToArray();

        return values.SequenceEqual(LowLod) ? GeometryDetail.Low :
            values.SequenceEqual(HighLod) ? GeometryDetail.High :
            GeometryDetail.Automatic;
    }

    public static void SetGeometryDetail(IDictionary<string, string> flags, GeometryDetail detail)
    {
        string[]? values = detail switch
        {
            GeometryDetail.Low => LowLod,
            GeometryDetail.High => HighLod,
            _ => null,
        };

        for (int i = 0; i < LodFlags.Length; i++)
            Set(flags, LodFlags[i], values?[i]);
    }

    // ---- lighting

    /// <summary>Stops the voxel lighting from updating as the world changes: more FPS, stale shadows.</summary>
    public static bool GetPauseVoxelizer(IDictionary<string, string> flags) => IsTrue(flags, PauseVoxelizer);

    public static void SetPauseVoxelizer(IDictionary<string, string> flags, bool enabled) =>
        Set(flags, PauseVoxelizer, enabled ? "True" : null);

    // ---- grass motion

    /// <summary>
    /// Grass stops swaying. The flag is the grass motion used with Roblox's "Reduced Motion" setting,
    /// so it only shows when that setting is on.
    /// </summary>
    public static bool GetStillGrass(IDictionary<string, string> flags) => Get(flags, GrassMotion) == "0";

    public static void SetStillGrass(IDictionary<string, string> flags, bool enabled) => Set(flags, GrassMotion, enabled ? "0" : null);

    // ---- one-click profiles

    /// <summary>Everything that costs FPS, down: lowest quality level and textures, no MSAA, grass or detail, frozen lighting.</summary>
    public static void ApplyPerformanceProfile(IDictionary<string, string> flags)
    {
        SetMsaa(flags, MsaaMode.X1);
        SetTextureQuality(flags, TextureQuality.Level0);
        SetQualityLevel(flags, 1);
        SetNoGrass(flags, true);
        SetGeometryDetail(flags, GeometryDetail.Low);
        SetPauseVoxelizer(flags, true);
    }

    /// <summary>Everything that looks better, up: highest quality level and textures, 4x MSAA, far detail.</summary>
    public static void ApplyQualityProfile(IDictionary<string, string> flags)
    {
        SetMsaa(flags, MsaaMode.X4);
        SetTextureQuality(flags, TextureQuality.Level3);
        SetQualityLevel(flags, MaxQualityLevel);
        SetNoGrass(flags, false);
        SetGeometryDetail(flags, GeometryDetail.High);
        SetPauseVoxelizer(flags, false);
    }

    // ---- helpers

    private static string? Get(IDictionary<string, string> flags, string name) =>
        flags.TryGetValue(name, out string? value) ? value : null;

    private static bool IsTrue(IDictionary<string, string> flags, string name) =>
        string.Equals(Get(flags, name), "True", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(IDictionary<string, string> flags, string name) =>
        string.Equals(Get(flags, name), "False", StringComparison.OrdinalIgnoreCase);

    private static void Set(IDictionary<string, string> flags, string name, string? value)
    {
        if (value is null)
            flags.Remove(name);
        else
            flags[name] = value;
    }
}
