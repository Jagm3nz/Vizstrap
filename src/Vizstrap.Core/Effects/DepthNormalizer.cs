namespace Vizstrap.Core.Effects;

/// <summary>
/// The depth AI gives relative inverse depth ("closeness") on a scale of its own that changes from frame
/// to frame. This maps it to 0 (far) … 1 (near) between the 2nd and 98th percentile, with a range that
/// follows the scene slowly, so the effects don't pump when something walks into view.
/// </summary>
public sealed class DepthNormalizer(double follow = 0.2)
{
    private const int SampleStep = 7;

    private double? _low;
    private double? _high;

    public double Low => _low ?? 0;

    public double High => _high ?? 1;

    public void Normalize(Span<float> values)
    {
        if (values.IsEmpty)
            return;

        var samples = new float[(values.Length + SampleStep - 1) / SampleStep];

        for (int i = 0, j = 0; i < values.Length; i += SampleStep, j++)
            samples[j] = values[i];

        Array.Sort(samples);
        double low = samples[(int)(samples.Length * 0.02)];
        double high = samples[Math.Min(samples.Length - 1, (int)(samples.Length * 0.98))];

        _low = _low is { } previousLow ? previousLow + (low - previousLow) * follow : low;
        _high = _high is { } previousHigh ? previousHigh + (high - previousHigh) * follow : high;

        float from = (float)_low.Value;
        float range = (float)Math.Max(_high.Value - _low.Value, 1e-4);

        for (int i = 0; i < values.Length; i++)
            values[i] = Math.Clamp((values[i] - from) / range, 0f, 1f);
    }
}

/// <summary>Depth sums shared with the shaders (Effects.hlsl) and the host.</summary>
public static class DepthMath
{
    public const float FarthestView = 60;

    /// <summary>Closeness 0 (far) … 1 (near) as view depth 60 … 1, as the shaders do it.</summary>
    public static float ViewDepth(float closeness) =>
        1 / (1 / FarthestView + (1 - 1 / FarthestView) * Math.Clamp(closeness, 0, 1));

    /// <summary>The median closeness of the middle tenth of the map: what the player looks at.</summary>
    public static float FocusCloseness(ReadOnlySpan<float> closeness, int width, int height)
    {
        int left = width * 45 / 100, right = Math.Max(left + 1, width * 55 / 100);
        int top = height * 45 / 100, bottom = Math.Max(top + 1, height * 55 / 100);
        var values = new List<float>((right - left) * (bottom - top));

        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                values.Add(closeness[y * width + x]);

        values.Sort();
        return values[values.Count / 2];
    }
}
