# 🌈 Making shaders

A shader in Vizstrap is a small function that decides the colour of **every pixel** of the game, 60 times a second, on
the graphics card. Yours run after Vizstrap's own effects, over the game - Roblox itself is never changed.

Shaders come in [packages](making-a-package.md). This guide shows how to write one, from a one-liner to effects that use
depth.

---

## 🚀 Quick start

1. Make a package: **Mods → Mod packages → Make your own → New package**. It already has a shader:
   `effects\scanlines.hlsl` and its sliders in `effects\scanlines.json`.
2. Add your own `effects\my-effect.hlsl` (and `effects\my-effect.json` for sliders).
3. **Install a package → Folder**, pick the folder, **Save**, reopen the settings.
4. **Shaders → From mod packages**: switch your effect on and move its sliders.
5. **Preview** shows before/after on a frame from your last game while you change the sliders - no need to start Roblox.

If a shader has a mistake, the error with its line number shows right under it on the Shaders page.

## ✍️ The function

A shader file holds one function, `Effect`:

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    return colour;   // the pixel's new colour
}
```

| | Meaning |
|---|---|
| `uv` | Where the pixel is: `(0, 0)` top left … `(1, 1)` bottom right |
| `colour` | The pixel's colour after Vizstrap's effects: red, green, blue from `0` to `1` |
| return | The new colour; values outside `0 … 1` are cut off |

You can add your own helper functions above `Effect`.

### What you can use

| Name | What it gives |
|---|---|
| `Picture(uv)` | The colour anywhere on the screen - for blurs, outlines, shifting the picture |
| `ViewDepth(uv)` | How far away that pixel is, guessed by the depth AI: about `1` (near) … `60` (far); `0` when the depth AI is off |
| `Luma(colour)` | How bright a colour is, `0 … 1` |
| `Time` | Seconds, for movement |
| `OutputSize` | The picture's size in pixels; one pixel in `uv` is `1 / OutputSize` |
| `Param0` … `Param7` | Your sliders |

> [!NOTE]
> Colours are in **linear light** - the way light adds up in the real world. Mixing and brightening look natural, but a
> "half grey" is `0.21`, not `0.5`. Vizstrap converts back for the screen.

## 🎚️ Sliders

`effects\my-effect.json` names the effect and up to 8 sliders. In the shader they're `Param0`, `Param1`… in this order:

```json
{
  "name": "My effect",
  "parameters": [
    { "name": "Strength", "min": 0, "max": 1, "default": 0.5 },
    { "name": "Size", "min": 1, "max": 20, "default": 4 }
  ]
}
```

Without a `.json` file the effect is named after its file and has no sliders.

## 🧪 Examples

Each one is a whole `.hlsl` file - copy it into `effects\` and give it a `.json` with the sliders it uses.

### 1. Warm tint - `Param0` = Strength

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float3 warm = colour * float3(1.1, 1.0, 0.85);
    return lerp(colour, warm, Param0);
}
```

`lerp(a, b, t)` mixes: `t = 0` gives `a`, `t = 1` gives `b`. Sliders from 0 to 1 are made for it.

### 2. Black and white - `Param0` = Strength

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float grey = Luma(colour);
    return lerp(colour, grey.xxx, Param0);
}
```

`grey.xxx` repeats one number three times: a grey colour.

### 3. Vignette - `Param0` = Strength, `Param1` = Size

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float distance = length(uv - 0.5) * 2;                    // 0 in the middle, about 1.4 in the corners
    float dark = smoothstep(Param1, Param1 + 0.6, distance);  // 0 inside, 1 towards the edges
    return colour * (1 - dark * Param0);
}
```

`smoothstep(from, to, x)` goes smoothly from 0 to 1 while `x` moves from `from` to `to`.

### 4. Pixel art - `Param0` = Pixel size (1 … 20)

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float2 cells = OutputSize / max(Param0, 1);   // how many big pixels fit across and down
    float2 cell = (floor(uv * cells) + 0.5) / cells;
    return Picture(cell);
}
```

`Picture()` reads another place of the screen: here, the middle of each big pixel.

### 5. Colour fringes - `Param0` = Strength

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float2 shift = (uv - 0.5) * Param0 * 0.01;
    return float3(Picture(uv + shift).r, colour.g, Picture(uv - shift).b);
}
```

Red and blue are read a little apart, more towards the edges - like a cheap camera lens.

### 6. Fog with distance - `Param0` = Strength, `Param1` = Start

Needs the depth AI (**Shaders → Adjust → Use the depth AI**).

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float depth = ViewDepth(uv);
    if (depth == 0)
        return colour;   // the depth AI is off

    float fog = smoothstep(Param1, 60, depth) * Param0;
    return lerp(colour, float3(0.55, 0.6, 0.75), fog);
}
```

### 7. Pulse - `Param0` = Strength, `Param1` = Speed

```hlsl
float3 Effect(float2 uv, float3 colour)
{
    float pulse = sin(Time * Param1 * 6.2832) * 0.5 + 0.5;   // 0 … 1 and back, Param1 times a second
    return colour * (1 + pulse * Param0 * 0.3);
}
```

### 8. Outlines - `Param0` = Strength, `Param1` = Thickness

```hlsl
float Brightness(float2 uv) { return sqrt(Luma(Picture(uv))); }

float3 Effect(float2 uv, float3 colour)
{
    float2 step = Param1 / OutputSize;
    float across = Brightness(uv + float2(step.x, 0)) - Brightness(uv - float2(step.x, 0));
    float down = Brightness(uv + float2(0, step.y)) - Brightness(uv - float2(0, step.y));
    float edge = smoothstep(0.1, 0.3, length(float2(across, down)));
    return colour * (1 - edge * Param0);
}
```

A helper function above `Effect`, and 4 reads of the picture around each pixel: where brightness jumps, there's an edge.

For more, open the example package's shaders - **Synthwave**, **Toon outlines** and **Retro TV** in
[`examples/synthwave-pack/effects`](../examples/synthwave-pack/effects).

## ⚡ Tips

- **Keep it light.** The function runs for millions of pixels a second. A few dozen `Picture()` reads are fine;
  hundreds will slow the game down.
- **Order matters.** Package shaders run one after another, in the order of the list on the Shaders page, each on what the
  previous one drew.
- **Never divide by something that can be 0** - use `max(x, 0.0001)`.
- **Screenshots don't show shaders** - the overlay hides itself from capture. Use **Preview** or look at the game.
- <kbd>F8</kbd> in game switches all shaders off and on - the quickest before/after.

## 🔤 HLSL in one table

| You write | It means |
|---|---|
| `float`, `float2`, `float3` | 1, 2 or 3 numbers (`float3` is a colour or a position) |
| `colour.r`, `.g`, `.b` / `uv.x`, `.y` | One part of it |
| `float3(1, 0.5, 0)` | A new colour |
| `lerp(a, b, t)` | Mix from `a` to `b` |
| `saturate(x)` | Keep between 0 and 1 |
| `smoothstep(a, b, x)` | A smooth 0 → 1 between `a` and `b` |
| `min`, `max`, `abs`, `floor`, `frac`, `sin`, `pow`, `length`, `dot` | The usual maths |

The full language: [HLSL reference](https://learn.microsoft.com/windows/win32/direct3dhlsl/dx-graphics-hlsl-reference).
