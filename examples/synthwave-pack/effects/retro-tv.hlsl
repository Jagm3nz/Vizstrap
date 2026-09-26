// Retro TV: a curved screen, colour fringes, scanlines and dark corners.
// Param0 = Curvature, Param1 = Colour fringe, Param2 = Scanlines.
// The overlay can't move Roblox's buttons: with a lot of curvature, clicks land where things really are.
float3 Effect(float2 uv, float3 colour)
{
    // bend the picture like the glass of an old TV: the edges reach past the picture, and past it is black
    float2 centred = uv * 2 - 1;
    float2 bent = centred * (1 + Param0 * 0.15 * dot(centred, centred));
    float2 screen = bent * 0.5 + 0.5;

    if (any(screen < 0) || any(screen > 1))
        return 0;

    // red and blue slip apart towards the edges
    float2 slip = (screen - 0.5) * Param1 * 0.008;
    float3 tv = float3(Picture(screen + slip).r, Picture(screen).g, Picture(screen - slip).b);

    // scanlines, one every two pixels
    float rows = sin(screen.y * OutputSize.y * 3.14159) * 0.5 + 0.5;
    tv *= 1 - Param2 * (1 - rows) * 0.45;

    // dark corners
    float corners = smoothstep(1.5, 0.6, length(bent * float2(1, 1.15)));
    return tv * lerp(1, corners, 0.7);
}
