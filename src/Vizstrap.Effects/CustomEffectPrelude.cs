namespace Vizstrap.Effects;

/// <summary>
/// What a package's effect gets around its Effect() function: the sliders, helpers to read the picture
/// and the depth, and the pass that calls it (see Packages.TemplateTexts for the authors' side).
/// </summary>
internal static class CustomEffectPrelude
{
    public const int ParameterCount = 8;

    public const string Header = """

        // ---- a package's effect
        cbuffer CustomParams : register(b1)
        {
            float Param0, Param1, Param2, Param3, Param4, Param5, Param6, Param7;
        };

        // the picture (after Vizstrap's effects and earlier package effects), in linear light
        float3 Picture(float2 uv) { return ToLinear(Tex0.SampleLevel(Linear, uv, 0).rgb); }

        // view depth from the AI guess: 1 near … 60 far, or 0 when the depth AI is off
        float ViewDepth(float2 uv) { return UseDepth > 0 ? Tex1.SampleLevel(Linear, uv, 0).r : 0; }
        """;

    public const string Entry = """

        float4 PS_CustomEffect(VSOut i) : SV_Target
        {
            return float4(ToSrgb(saturate(Effect(i.Uv, Picture(i.Uv)))), 1);
        }
        """;
}
