namespace Vizstrap.Core.Effects;

public enum EffectPreset
{
    Realistic,
    Cinematic,
    Vivid,
    /// <summary>Without the depth AI: glow, colours and sharpening only, for slower graphics cards.</summary>
    Light,
    /// <summary>Sliders moved away from every preset.</summary>
    Custom,
}

/// <summary>
/// The picture effects drawn over Roblox (see Vizstrap.Effects): an approximation of ray-traced lighting
/// from depth guessed by an AI model, plus glow and colour. Strengths are what the sliders show: 0–100,
/// saturation and warmth −50…50.
/// </summary>
public sealed class EffectSettings
{
    public bool Enabled { get; set; }

    public EffectPreset Preset { get; set; } = EffectPreset.Realistic;

    /// <summary>Use the depth AI (shadows in corners, bounced light, haze, background blur).</summary>
    public bool UseDepth { get; set; } = true;

    /// <summary>Contact shadows in corners and under objects (screen-space ambient occlusion).</summary>
    public int AmbientOcclusion { get; set; } = 70;

    /// <summary>Colour bouncing off nearby surfaces (screen-space global illumination).</summary>
    public int IndirectLight { get; set; } = 45;

    /// <summary>Distance haze (aerial perspective).</summary>
    public int Haze { get; set; } = 20;

    /// <summary>Blur of what's far from the centre of the screen's depth.</summary>
    public int DepthOfField { get; set; }

    /// <summary>Mirror-like reflections on floors and water (screen-space).</summary>
    public int Reflections { get; set; } = 15;

    /// <summary>Light streaming from the bright sky (god rays).</summary>
    public int SunRays { get; set; } = 30;

    public int Bloom { get; set; } = 30;

    public int Contrast { get; set; } = 20;

    public int Saturation { get; set; } = 5;

    public int Warmth { get; set; } = 5;

    public int Vignette { get; set; } = 20;

    public int Sharpen { get; set; } = 20;

    /// <summary>Picture effects from mod packages (HLSL), drawn after these, in this order.</summary>
    public List<CustomEffectState> Custom { get; set; } = [];

    public EffectSettings Clone()
    {
        var clone = (EffectSettings)MemberwiseClone();
        clone.Custom = [.. Custom.Select(state => state with { Values = [.. state.Values] })];
        return clone;
    }

    /// <summary>The same look (the on/off switch and the preset's name don't count).</summary>
    public bool SameLookAs(EffectSettings other) =>
        UseDepth == other.UseDepth && AmbientOcclusion == other.AmbientOcclusion && IndirectLight == other.IndirectLight &&
        Haze == other.Haze && DepthOfField == other.DepthOfField && Reflections == other.Reflections && SunRays == other.SunRays &&
        Bloom == other.Bloom && Contrast == other.Contrast &&
        Saturation == other.Saturation && Warmth == other.Warmth && Vignette == other.Vignette && Sharpen == other.Sharpen;

    public bool SameAs(EffectSettings other) =>
        Enabled == other.Enabled && Preset == other.Preset && SameLookAs(other) &&
        Custom.Count == other.Custom.Count && Custom.Zip(other.Custom).All(pair => pair.First.SameAs(pair.Second));
}

/// <summary>A package's picture effect as the player set it: on or off and its sliders' values.</summary>
/// <param name="Id">"packageId/name".</param>
public sealed record CustomEffectState(string Id, bool Enabled, float[] Values)
{
    public bool SameAs(CustomEffectState other) =>
        Id == other.Id && Enabled == other.Enabled && Values.AsSpan().SequenceEqual(other.Values);
}

public static class EffectPresets
{
    public static IReadOnlyList<EffectPreset> All { get; } =
        [EffectPreset.Realistic, EffectPreset.Cinematic, EffectPreset.Vivid, EffectPreset.Light];

    /// <summary>The preset's look; <see cref="EffectPreset.Custom"/> has none and returns null.</summary>
    public static EffectSettings? Look(EffectPreset preset) => preset switch
    {
        EffectPreset.Realistic => new EffectSettings
        {
            Preset = preset, UseDepth = true, AmbientOcclusion = 70, IndirectLight = 45, Haze = 20, DepthOfField = 0,
            Reflections = 15, SunRays = 30, Bloom = 30, Contrast = 20, Saturation = 5, Warmth = 5, Vignette = 20, Sharpen = 20,
        },
        EffectPreset.Cinematic => new EffectSettings
        {
            Preset = preset, UseDepth = true, AmbientOcclusion = 65, IndirectLight = 35, Haze = 30, DepthOfField = 35,
            Reflections = 20, SunRays = 45, Bloom = 40, Contrast = 25, Saturation = -10, Warmth = 12, Vignette = 45, Sharpen = 10,
        },
        EffectPreset.Vivid => new EffectSettings
        {
            Preset = preset, UseDepth = true, AmbientOcclusion = 55, IndirectLight = 60, Haze = 10, DepthOfField = 0,
            Reflections = 25, SunRays = 35, Bloom = 35, Contrast = 20, Saturation = 18, Warmth = 0, Vignette = 15, Sharpen = 25,
        },
        EffectPreset.Light => new EffectSettings
        {
            Preset = preset, UseDepth = false, AmbientOcclusion = 0, IndirectLight = 0, Haze = 0, DepthOfField = 0,
            Reflections = 0, SunRays = 0, Bloom = 35, Contrast = 15, Saturation = 10, Warmth = 5, Vignette = 15, Sharpen = 25,
        },
        _ => null,
    };

    /// <summary>The look after this one, round and round (F7 in game); a custom look goes to the first.</summary>
    public static EffectPreset Next(EffectPreset preset)
    {
        int index = All.ToList().IndexOf(preset);
        return All[(index + 1) % All.Count];
    }

    /// <summary>The preset whose look this is, or <see cref="EffectPreset.Custom"/>.</summary>
    public static EffectPreset Match(EffectSettings settings) =>
        All.FirstOrDefault(preset => Look(preset)!.SameLookAs(settings), EffectPreset.Custom);
}
