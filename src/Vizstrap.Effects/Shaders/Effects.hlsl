// Vizstrap's picture effects. Every pass draws one full-screen triangle with a pixel shader below.
// The captured picture is sRGB; light is added and mixed in linear light and encoded again at the end.
// Depth comes from an AI guess as "closeness" 0 (far) … 1 (near) and is turned into a view depth of
// 1 … 60 units; the effects only need it to be roughly right relative to the neighbouring pixels.
// Noisy effects (contact shadows, bounced light, reflections) are accumulated over frames with the
// history clamped to the current neighbourhood, so they settle without smearing when the view moves.

cbuffer Params : register(b0)
{
    float2 OutputSize;      // the full picture
    float2 OutputTexel;     // 1 / OutputSize
    float2 SourceTexel;     // 1 / size of the pass's input (down/up sampling, blurs)
    float2 BlurDirection;   // (1, 0) or (0, 1)
    float AoStrength;       // 0 … 1
    float GiStrength;       // 0 … 1
    float Haze;             // 0 … 1
    float DepthOfField;     // 0 … 1
    float Bloom;            // 0 … 1
    float Contrast;         // 0 … 1
    float Saturation;       // -0.5 … 0.5
    float Warmth;           // -0.5 … 0.5
    float Vignette;         // 0 … 1
    float Sharpen;          // 0 … 1
    float FocusDepth;       // view depth in focus (the middle of the screen)
    float Time;             // seconds, for the dithering pattern
    float UseDepth;         // 1 when a depth map is there
    float TanHalfFov;       // Roblox's default 70° vertical field of view
    float Aspect;           // width / height
    float UiBand;           // Roblox's top bar, as a share of the height: depth effects stay out of it
    float Reflections;      // 0 … 1
    float SunRays;          // 0 … 1
    float FrameIndex;       // turns the sampling pattern every frame
    float HistoryValid;     // 0 on the first frame after a reset
    float SunMip;           // the 1×1 level of the sun centroid texture
    float HasDepthPicture;  // 1 when the picture the depth AI saw is bound
    float Padding0;
    float Padding1;
};

Texture2D Tex0 : register(t0);
Texture2D Tex1 : register(t1);
Texture2D Tex2 : register(t2);
Texture2D Tex3 : register(t3);
Texture2D Tex4 : register(t4);
Texture2D Tex5 : register(t5);
Texture2D Tex6 : register(t6);
Texture2D Tex7 : register(t7);
Texture2D Tex8 : register(t8);
SamplerState Linear : register(s0);
SamplerState Nearest : register(s1);

struct VSOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

VSOut VS(uint id : SV_VertexID)
{
    VSOut o;
    o.Uv = float2((id << 1) & 2, id & 2);
    o.Position = float4(o.Uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

// ---- helpers

float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

float3 ToSrgb(float3 c)
{
    c = saturate(c);
    return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1 / 2.4) - 0.055;
}

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// interleaved gradient noise (Jimenez): cheap, even, changes every pixel
float Noise(float2 pixel) { return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))); }

// "screen" blend: adds light without ever going past white, and does nothing when the light is 0
float3 Screen(float3 c, float3 light) { return 1 - (1 - saturate(c)) * (1 - saturate(light)); }

float DepthToView(float closeness) { return 1 / lerp(1.0 / 60, 1.0, saturate(closeness)); }

// view-space position from the screen position and view depth (the camera looks down +z)
float3 ViewPosition(float2 uv, float z)
{
    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    return float3(ndc.x * TanHalfFov * Aspect * z, ndc.y * TanHalfFov * z, z);
}

// and back: where a view-space point lands on the screen
float2 ScreenPosition(float3 p)
{
    float2 ndc = float2(p.x / (TanHalfFov * Aspect * p.z), p.y / (TanHalfFov * p.z));
    return float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}

float3 ViewAt(float2 uv) { return ViewPosition(uv, Tex0.SampleLevel(Nearest, uv, 0).r); }

// surface normal from the depth around a pixel, taking the smaller step on each axis so edges stay sharp
float3 NormalAt(float2 uv, float3 p, float2 texel)
{
    float3 right = ViewAt(uv + float2(texel.x, 0)) - p;
    float3 left = p - ViewAt(uv - float2(texel.x, 0));
    float3 down = ViewAt(uv + float2(0, texel.y)) - p;
    float3 up = p - ViewAt(uv - float2(0, texel.y));

    float3 dx = abs(right.z) < abs(left.z) ? right : left;
    float3 dy = abs(down.z) < abs(up.z) ? down : up;
    float3 n = normalize(cross(dy, dx));
    return n.z > 0 ? -n : n;
}

// ---- depth: the AI's small map brought to half resolution, following the picture's edges, eased over
// frames, with a confidence that drops where the picture moved since the AI looked at it
// Tex0: captured picture (sRGB), Tex1: closeness map, Tex2: last frame's depth, Tex3: picture at 1/4 (linear),
// Tex4: the picture the AI saw (sRGB). Out: r = view depth, g = confidence.

float2 PS_Depth(VSOut i) : SV_Target
{
    uint width, height;
    Tex1.GetDimensions(width, height);
    float2 lowSize = float2(width, height);

    float2 position = i.Uv * lowSize - 0.5;
    float2 base = floor(position);
    float2 f = position - base;
    float guide = Luma(Tex0.SampleLevel(Linear, i.Uv, 0).rgb);

    float sum = 0, weights = 0;

    [unroll] for (int y = 0; y < 2; y++)
    [unroll] for (int x = 0; x < 2; x++)
    {
        float2 uv = (base + float2(x, y) + 0.5) / lowSize;
        float bilinear = (x ? f.x : 1 - f.x) * (y ? f.y : 1 - f.y);
        float similarity = exp(-abs(Luma(Tex0.SampleLevel(Linear, uv, 0).rgb) - guide) * 12);
        float weight = bilinear * similarity + 1e-4;
        sum += Tex1.SampleLevel(Nearest, uv, 0).r * weight;
        weights += weight;
    }

    float z = DepthToView(sum / weights);

    // where the picture changed since the AI saw it, its depth is stale there
    float confidence = 1;

    if (HasDepthPicture > 0)
    {
        float now = Luma(Tex3.SampleLevel(Linear, i.Uv, 0).rgb);
        float then = Luma(ToLinear(Tex4.SampleLevel(Linear, i.Uv, 0).rgb));
        confidence = saturate(1 - abs(now - then) * 6);
    }

    if (HistoryValid > 0)
    {
        float2 previous = Tex2.SampleLevel(Nearest, i.Uv, 0).rg;

        // small wobbles are smoothed away, real changes come through at once
        float change = abs(z - previous.r) / z;
        z = lerp(previous.r, z, saturate(0.3 + change * 3));
        confidence = lerp(previous.g, confidence, 0.35);
    }

    return float2(z, confidence);
}

// ---- lighting at half resolution: bounced light (rgb) and how open each point is (a)
// Tex0: view depth, Tex1: the picture blurred to a quarter (linear) as the light that bounces

static const int AoSamples = 12;
static const int GiSamples = 12;
static const float GoldenAngle = 2.39996323;

float4 PS_Lighting(VSOut i) : SV_Target
{
    uint width, height;
    Tex0.GetDimensions(width, height);
    float2 texel = 1 / float2(width, height);

    float z = Tex0.SampleLevel(Nearest, i.Uv, 0).r;
    float3 p = ViewPosition(i.Uv, z);
    float3 n = NormalAt(i.Uv, p, texel);

    // the pattern turns every frame; the frames are averaged afterwards
    float angle = (Noise(i.Position.xy) + FrameIndex * 0.618034) * 6.2831853;
    float jitter = frac(Noise(i.Position.yx + 23.0) + FrameIndex * 0.7548777);
    float2 toUv = float2(1 / Aspect, 1);

    // contact shadows (Alchemy ambient occlusion), a disc a fixed share of the screen wide
    float occlusion = 0;

    [unroll] for (int s = 0; s < AoSamples; s++)
    {
        float t = (s + jitter) / AoSamples;
        float a = angle + s * GoldenAngle;
        float2 uv = i.Uv + float2(cos(a), sin(a)) * sqrt(t) * 0.05 * toUv;
        float zs = Tex0.SampleLevel(Nearest, uv, 0).r;
        float3 v = ViewPosition(uv, zs) - p;

        // a person against the sky doesn't shadow the sky
        float range = saturate(1 - abs(zs - z) / (0.3 * z));
        occlusion += max(0, dot(v, n) - 0.01 * z) / (dot(v, v) + 0.0004 * z * z) * range * z;
    }

    float openness = pow(saturate(1 - occlusion * 0.12 / AoSamples), 1.4);

    // bounced light: the average light of nearby surfaces facing this point, fading with distance
    float3 bounce = 0;
    float reached = 0;

    [unroll] for (int g = 0; g < GiSamples; g++)
    {
        float t = (g + jitter) / GiSamples;
        float a = angle + g * GoldenAngle + 1.3;
        float2 uv = i.Uv + float2(cos(a), sin(a)) * sqrt(t) * 0.14 * toUv;
        float zs = Tex0.SampleLevel(Nearest, uv, 0).r;
        float3 v = ViewPosition(uv, zs) - p;
        float distance = length(v) + 1e-4;
        float cosine = saturate(dot(n, v / distance));
        float reach = distance / (0.18 * z);
        float falloff = 1 / (1 + reach * reach);
        float range = saturate(1 - abs(zs - z) / (0.8 * z));

        float weight = cosine * falloff * range;
        bounce += Tex1.SampleLevel(Linear, uv, 0).rgb * weight;
        reached += weight;
    }

    // how much of the surroundings actually faces this point
    float coverage = saturate(reached / (GiSamples * 0.25));
    return float4(bounce / max(reached, 1e-3) * coverage, openness);
}

// ---- reflections at half resolution: rays from floors and water, marched through the depth
// Tex0: view depth, Tex1: the picture at 1/2 (linear). Out: rgb = what's reflected, a = how much.

static const int ReflectionSteps = 20;

float4 PS_Reflections(VSOut i) : SV_Target
{
    uint width, height;
    Tex0.GetDimensions(width, height);
    float2 texel = 1 / float2(width, height);

    float z = Tex0.SampleLevel(Nearest, i.Uv, 0).r;
    float3 p = ViewPosition(i.Uv, z);

    // the normal from a wider neighbourhood: the guessed depth is too bumpy up close for mirrors
    float3 n = NormalAt(i.Uv, p, texel * 3);

    // only surfaces facing up (floors, water, roads) and only below Roblox's top bar
    float facingUp = saturate((n.y - 0.6) / 0.25);

    if (facingUp <= 0 || i.Uv.y < UiBand)
        return 0;

    // smooth surfaces reflect: little detail in the picture around the point
    float centre = Luma(Tex1.SampleLevel(Linear, i.Uv, 0).rgb);
    float detail = 0;

    [unroll] for (int k = 0; k < 4; k++)
    {
        float2 offset = float2(k < 2 ? (k * 2 - 1) : 0, k >= 2 ? (k * 2 - 5) : 0) * texel * 2;
        detail += abs(Luma(Tex1.SampleLevel(Linear, i.Uv + offset, 0).rgb) - centre);
    }

    float smooth = saturate(1 - detail / max(centre, 0.05) * 1.5);

    float3 view = normalize(p);
    float3 ray = reflect(view, n);

    // a random start per pixel and frame hides the steps; the frames and a narrow blur even it out
    float jitter = frac(Noise(i.Position.xy) + FrameIndex * 0.618034);

    // steps that grow with distance, starting a little away from the surface
    float travelled = 0.02 * z;
    float step = 0.05 * z;

    [loop] for (int s = 0; s < ReflectionSteps; s++)
    {
        travelled += step * (s == 0 ? jitter : 1);
        step *= 1.15;

        float3 q = p + ray * travelled;

        if (q.z <= 0.5)
            break;

        float2 uv = ScreenPosition(q);

        if (any(uv < 0) || any(uv > 1))
            break;

        float surface = Tex0.SampleLevel(Nearest, uv, 0).r;
        float behind = q.z - surface;

        // the ray went just behind something: that's what it hits
        if (behind > 0 && behind < 0.15 * surface + step)
        {
            // narrow the hit down between the last two points, for a sharp reflection
            float low = travelled - step, high = travelled;

            [unroll] for (int r = 0; r < 5; r++)
            {
                float middle = (low + high) * 0.5;
                float3 m = p + ray * middle;
                float2 muv = ScreenPosition(m);

                if (m.z > Tex0.SampleLevel(Nearest, muv, 0).r)
                    high = middle;
                else
                    low = middle;
            }

            uv = ScreenPosition(p + ray * high);
            float2 edge = saturate(min(uv, 1 - uv) * 8);
            float fade = edge.x * edge.y * (1 - (float)s / ReflectionSteps);

            // the depth guess is least sure far away, so reflections stay close
            float near = saturate(1 - (z - 8) / 25);
            float cosine = saturate(dot(-view, n));
            float fresnel = 0.15 + 0.85 * pow(1 - cosine, 5);
            return float4(Tex1.SampleLevel(Linear, uv, 0).rgb, fade * fresnel * smooth * facingUp * near);
        }
    }

    return 0;
}

// ---- accumulation over frames, with the last result held to what's around now (no smearing)
// Tex0: this frame (noisy), Tex1: the accumulated result so far

float4 PS_Accumulate(VSOut i) : SV_Target
{
    float4 current = Tex0.SampleLevel(Nearest, i.Uv, 0);

    if (HistoryValid <= 0)
        return current;

    float4 low = current, high = current;

    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float4 around = Tex0.SampleLevel(Nearest, i.Uv + float2(x, y) * SourceTexel, 0);
        low = min(low, around);
        high = max(high, around);
    }

    float4 history = clamp(Tex1.SampleLevel(Nearest, i.Uv, 0), low, high);
    return lerp(history, current, 0.2);
}

// ---- blur along one axis that keeps to surfaces of the same depth
// Tex0: what's blurred, Tex1: view depth

float4 PS_BlurLighting(VSOut i) : SV_Target
{
    float z = Tex1.SampleLevel(Nearest, i.Uv, 0).r;
    float4 sum = 0;
    float weights = 0;

    [unroll] for (int k = -4; k <= 4; k++)
    {
        float2 uv = i.Uv + BlurDirection * SourceTexel * (k * 1.25);
        float zs = Tex1.SampleLevel(Nearest, uv, 0).r;
        float weight = exp(-k * k / 12.0) * exp(-abs(zs - z) / (0.04 * z));
        sum += Tex0.SampleLevel(Linear, uv, 0) * weight;
        weights += weight;
    }

    return sum / weights;
}

// ---- sun rays: bright, far parts of the picture, streaked towards where the light comes from
// Tex0: the picture at 1/4 (linear), Tex1: view depth

float3 SunLight(float2 uv)
{
    float3 c = Tex0.SampleLevel(Linear, uv, 0).rgb;
    float far = saturate((Tex1.SampleLevel(Linear, uv, 0).r - 20) / 25);
    float bright = saturate((Luma(c) - 0.65) / 0.25);
    return c * bright * far;
}

float4 PS_SunLight(VSOut i) : SV_Target { return float4(SunLight(i.Uv), 1); }

// the same, weighted by where it is, to find the light's middle from the smallest mip
float4 PS_SunCentre(VSOut i) : SV_Target
{
    float weight = Luma(SunLight(i.Uv));
    return float4(i.Uv * weight, weight, 1);
}

// Tex0: sun light (1/4), Tex1: sun centre (1/4, with mips)
float4 PS_SunRays(VSOut i) : SV_Target
{
    float3 centre = Tex1.SampleLevel(Linear, float2(0.5, 0.5), SunMip).xyz;

    // not enough bright sky for rays
    if (centre.z < 1e-4)
        return 0;

    float2 light = centre.xy / centre.z;
    float strength = saturate(centre.z * 25);
    float2 toward = (light - i.Uv) / 40;
    float jitter = Noise(i.Position.xy);
    float2 uv = i.Uv + toward * jitter;
    float3 sum = 0;
    float decay = 1;

    [loop] for (int s = 0; s < 40; s++)
    {
        sum += Tex0.SampleLevel(Linear, uv, 0).rgb * decay;
        decay *= 0.95;
        uv += toward;
    }

    return float4(sum / 40 * strength * 1.8, 1);
}

// ---- down and up sampling (Bjørge's dual filter), used for the blurred picture and the glow

float3 DownSample(float2 uv, bool srgb)
{
    float2 o = SourceTexel;
    float3 c0 = Tex0.SampleLevel(Linear, uv, 0).rgb;
    float3 c1 = Tex0.SampleLevel(Linear, uv + float2(-o.x, -o.y), 0).rgb;
    float3 c2 = Tex0.SampleLevel(Linear, uv + float2(o.x, -o.y), 0).rgb;
    float3 c3 = Tex0.SampleLevel(Linear, uv + float2(-o.x, o.y), 0).rgb;
    float3 c4 = Tex0.SampleLevel(Linear, uv + float2(o.x, o.y), 0).rgb;

    if (srgb)
    {
        c0 = ToLinear(c0);
        c1 = ToLinear(c1);
        c2 = ToLinear(c2);
        c3 = ToLinear(c3);
        c4 = ToLinear(c4);
    }

    return (c0 * 4 + c1 + c2 + c3 + c4) / 8;
}

// Tex0: the captured picture (sRGB)
float4 PS_DownFirst(VSOut i) : SV_Target { return float4(DownSample(i.Uv, true), 1); }

// Tex0: a linear level
float4 PS_Down(VSOut i) : SV_Target { return float4(DownSample(i.Uv, false), 1); }

// Tex0: the smaller level, Tex1: this level's own glow, added on
float4 PS_UpAdd(VSOut i) : SV_Target
{
    float2 o = SourceTexel;
    float3 sum = Tex0.SampleLevel(Linear, i.Uv + float2(-o.x * 2, 0), 0).rgb;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(-o.x, o.y), 0).rgb * 2;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(0, o.y * 2), 0).rgb;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(o.x, o.y), 0).rgb * 2;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(o.x * 2, 0), 0).rgb;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(o.x, -o.y), 0).rgb * 2;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(0, -o.y * 2), 0).rgb;
    sum += Tex0.SampleLevel(Linear, i.Uv + float2(-o.x, -o.y), 0).rgb * 2;
    return float4(sum / 12 + Tex1.SampleLevel(Linear, i.Uv, 0).rgb, 1);
}

// what glows: the bright parts of the half-size picture, with a soft knee (Tex0)
float4 PS_BrightPass(VSOut i) : SV_Target
{
    float3 c = Tex0.SampleLevel(Linear, i.Uv, 0).rgb;
    float brightness = max(c.r, max(c.g, c.b));
    const float threshold = 0.75, knee = 0.2;
    float soft = clamp(brightness - threshold + knee, 0, 2 * knee);
    soft = soft * soft / (4 * knee);
    float contribution = max(soft, brightness - threshold) / max(brightness, 1e-4);
    return float4(c * contribution, 1);
}

// ---- a linear level (Tex0) as an sRGB picture: what the depth AI sees, and the preview frame

float4 PS_ToPicture(VSOut i) : SV_Target { return float4(ToSrgb(Tex0.SampleLevel(Linear, i.Uv, 0).rgb), 1); }

// ---- everything together
// Tex0: captured picture (sRGB), Tex1: depth (view depth, confidence), Tex2: lighting, Tex3: picture at 1/2,
// Tex4: picture at 1/4, Tex5: picture at 1/8, Tex6: glow, Tex7: reflections, Tex8: sun rays

float4 PS_Composite(VSOut i) : SV_Target
{
    float2 uv = i.Uv;
    float3 c = ToLinear(Tex0.SampleLevel(Nearest, uv, 0).rgb);

    // sharpening that never goes past its neighbours, so no halos
    if (Sharpen > 0)
    {
        float3 n = ToLinear(Tex0.SampleLevel(Nearest, uv + float2(0, -OutputTexel.y), 0).rgb);
        float3 s = ToLinear(Tex0.SampleLevel(Nearest, uv + float2(0, OutputTexel.y), 0).rgb);
        float3 e = ToLinear(Tex0.SampleLevel(Nearest, uv + float2(OutputTexel.x, 0), 0).rgb);
        float3 w = ToLinear(Tex0.SampleLevel(Nearest, uv + float2(-OutputTexel.x, 0), 0).rgb);
        float3 low = min(c, min(min(n, s), min(e, w)));
        float3 high = max(c, max(max(n, s), max(e, w)));
        c = clamp(c + (c - (n + s + e + w) * 0.25) * Sharpen * 1.5, low, high);
    }

    if (UseDepth > 0)
    {
        // Roblox's top bar keeps its look; where the depth is stale the effects step back
        float2 depth = Tex1.SampleLevel(Linear, uv, 0).rg;
        float z = depth.r;
        float trust = smoothstep(UiBand, UiBand + 0.03, uv.y) * lerp(0.35, 1, depth.g);
        float4 lighting = Tex2.SampleLevel(Linear, uv, 0);

        // bounced light spills into what's darker than the light around it, tinted by what it came off
        c += max(0, lighting.rgb - c) * GiStrength * 0.7 * trust;

        // then the corners and contact points darken
        c *= lerp(1, lighting.a, AoStrength * trust);

        // reflections on floors and water
        float4 reflection = Tex7.SampleLevel(Linear, uv, 0);
        c = lerp(c, reflection.rgb, saturate(reflection.a * Reflections * 1.6) * trust);

        // background blur grows with the distance from what's in focus
        float coc = saturate(abs(z - FocusDepth) / max(FocusDepth, 1) * 1.2) * DepthOfField * trust;
        float3 near = Tex3.SampleLevel(Linear, uv, 0).rgb;
        float3 far = Tex4.SampleLevel(Linear, uv, 0).rgb;
        c = lerp(c, lerp(near, far, saturate(coc * 2 - 1)), saturate(coc * 2));

        // distance haze: far things soften towards the light around them
        float distance = saturate((z - 6) / 50);
        float haze = Haze * (1 - exp(-distance * 3)) * trust;
        float3 air = Tex5.SampleLevel(Linear, uv, 0).rgb * 1.05 + 0.01;
        c = lerp(c, air, haze * 0.5);

        // light streaming from the bright sky
        c = Screen(c, Tex8.SampleLevel(Linear, uv, 0).rgb * SunRays * 1.2);
    }

    // glow around the bright parts
    c = Screen(c, Tex6.SampleLevel(Linear, uv, 0).rgb * Bloom * 0.9);

    // colour: saturation, then warmth (a white balance shift)
    c = max(0, lerp(Luma(c).xxx, c, 1 + Saturation * 1.2));
    c *= float3(1 + Warmth * 0.14, 1 + Warmth * 0.03, 1 - Warmth * 0.14);

    // vignette
    float2 centred = (uv - 0.5) * float2(Aspect, 1);
    float edge = length(centred) / length(float2(Aspect, 1) * 0.5);
    c *= 1 - Vignette * 0.55 * pow(edge, 2.4);

    // contrast as an S-curve on the encoded picture: black, white and middle grey stay put
    float3 encoded = ToSrgb(c);
    encoded = lerp(encoded, encoded * encoded * (3 - 2 * encoded), Contrast);

    // dithering keeps the gradients free of banding
    encoded += (Noise(i.Position.xy + frac(Time) * 60) - 0.5) / 255;
    return float4(encoded, 1);
}
