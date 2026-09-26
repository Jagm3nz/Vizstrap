using Vizstrap.Core.Packages;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Vizstrap.Effects;

/// <summary>Packages' effects read from their .hlsl files and compiled; the compiler's messages say what's wrong.</summary>
public static class CustomEffectSources
{
    /// <summary>Compiles the effects into the renderer; returns a message for each one that can't be used.</summary>
    public static IReadOnlyDictionary<string, string> Load(EffectRenderer renderer, IReadOnlyList<CustomEffect>? effects)
    {
        if (effects is null or { Count: 0 })
            return new Dictionary<string, string>();

        var sources = new List<(string Id, string Source)>();
        var errors = new Dictionary<string, string>();

        foreach (var effect in effects)
        {
            try
            {
                sources.Add((effect.Id, File.ReadAllText(effect.SourcePath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors[effect.Id] = ex.Message;
            }
        }

        foreach (var (id, error) in renderer.LoadCustomEffects(sources))
            errors[id] = error;

        return errors;
    }

    /// <summary>
    /// Checks that the effects compile, on Windows' software renderer (no graphics card needed), for the
    /// settings to show; an empty result means all is well.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Check(IReadOnlyList<CustomEffect> effects)
    {
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.None, [FeatureLevel.Level_11_0], out ID3D11Device? device).CheckError();

        using (device!)
        using (var renderer = new EffectRenderer(device!))
            return Load(renderer, effects);
    }
}
