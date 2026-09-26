// Synthwave: shadows lean violet, light leans cyan, colours get richer and bright spots glow pink.
// Param0 = Strength, Param1 = Glow. Colours are in linear light (0 … 1).
float3 Effect(float2 uv, float3 colour)
{
    float light = Luma(colour);

    // the tint moves from violet in the dark to cyan in the light; the brightness stays
    float3 violet = float3(0.30, 0.08, 0.85);
    float3 cyan = float3(0.15, 0.85, 1.00);
    float3 tint = lerp(violet, cyan, smoothstep(0.02, 0.5, light));
    float3 graded = lerp(colour, light * tint / Luma(tint), 0.35);

    // richer colours
    graded = max(0, lerp(Luma(graded).xxx, graded, 1.3));

    // glow: what's brighter than 0.5 in two rings around the pixel, in pink
    float2 pixel = 1 / OutputSize;
    float3 glow = 0;

    [unroll] for (int i = 0; i < 12; i++)
    {
        float angle = i * 0.5236;
        float2 direction = float2(cos(angle), sin(angle));
        glow += max(0, Picture(uv + direction * pixel * 5) - 0.5);
        glow += max(0, Picture(uv + direction * pixel * 13) - 0.5) * 0.6;
    }

    graded += glow / 12 * Param1 * float3(1.0, 0.35, 0.85);
    return lerp(colour, graded, Param0);
}
