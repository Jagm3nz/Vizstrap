// Toon outlines: dark lines where the colour or the depth jumps, like a cartoon.
// Param0 = Strength, Param1 = Thickness (pixels), Param2 = Depth edges (needs the depth AI).

// brightness as the eye sees it, so dark areas get lines too
float Tone(float2 uv) { return sqrt(Luma(Picture(uv))); }

float3 Effect(float2 uv, float3 colour)
{
    float2 step = Param1 / OutputSize;

    // Sobel: how fast the brightness changes across and down
    float tl = Tone(uv + step * float2(-1, -1)), t = Tone(uv + step * float2(0, -1)), tr = Tone(uv + step * float2(1, -1));
    float l = Tone(uv + step * float2(-1, 0)), r = Tone(uv + step * float2(1, 0));
    float bl = Tone(uv + step * float2(-1, 1)), b = Tone(uv + step * float2(0, 1)), br = Tone(uv + step * float2(1, 1));
    float across = (tr + 2 * r + br) - (tl + 2 * l + bl);
    float down = (bl + 2 * b + br) - (tl + 2 * t + tr);
    float colourEdge = smoothstep(0.25, 0.6, sqrt(across * across + down * down));

    // depth: a neighbour much nearer or further than this pixel is an object's outline
    float depthEdge = 0;
    float depth = ViewDepth(uv);

    if (depth > 0)
    {
        float jump = max(max(abs(ViewDepth(uv - float2(step.x, 0)) - depth), abs(ViewDepth(uv + float2(step.x, 0)) - depth)),
                         max(abs(ViewDepth(uv - float2(0, step.y)) - depth), abs(ViewDepth(uv + float2(0, step.y)) - depth)));
        depthEdge = smoothstep(0.06, 0.15, jump / depth) * Param2;
    }

    float edge = saturate(max(colourEdge, depthEdge));
    return colour * (1 - edge * Param0 * 0.9);
}
