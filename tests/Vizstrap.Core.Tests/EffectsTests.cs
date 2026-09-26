using System.Security.Cryptography;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Tests.TestSupport;
using Vizstrap.Effects;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Vizstrap.Core.Tests;

public class EffectPresetsTests
{
    [Fact]
    public void Every_preset_is_recognised_from_its_look()
    {
        foreach (var preset in EffectPresets.All)
            Assert.Equal(preset, EffectPresets.Match(EffectPresets.Look(preset)!));
    }

    [Fact]
    public void A_moved_slider_makes_the_look_custom()
    {
        var look = EffectPresets.Look(EffectPreset.Realistic)!;
        look.Bloom += 5;

        Assert.Equal(EffectPreset.Custom, EffectPresets.Match(look));
        Assert.Null(EffectPresets.Look(EffectPreset.Custom));
    }

    [Fact]
    public void Effects_start_off_with_the_realistic_look()
    {
        var settings = new EffectSettings();

        Assert.False(settings.Enabled);
        Assert.Equal(EffectPreset.Realistic, EffectPresets.Match(settings));
        Assert.True(settings.SameLookAs(EffectPresets.Look(EffectPreset.Realistic)!));
        Assert.False(EffectPresets.Look(EffectPreset.Light)!.UseDepth);
    }

    [Fact]
    public void F7_goes_round_the_looks()
    {
        Assert.Equal(EffectPreset.Cinematic, EffectPresets.Next(EffectPreset.Realistic));
        Assert.Equal(EffectPreset.Realistic, EffectPresets.Next(EffectPresets.All[^1]));
        Assert.Equal(EffectPreset.Realistic, EffectPresets.Next(EffectPreset.Custom));
    }

    [Fact]
    public void Shader_strengths_come_from_the_sliders()
    {
        var parameters = EffectParameters.From(new EffectSettings { AmbientOcclusion = 50, Saturation = -20, UseDepth = false });

        Assert.Equal(0.5f, parameters.AmbientOcclusion);
        Assert.Equal(-0.2f, parameters.Saturation);
        Assert.False(parameters.UseDepth);
    }
}

public class DepthTests
{
    [Fact]
    public void Closeness_is_spread_between_the_2nd_and_98th_percentile()
    {
        var values = Enumerable.Range(0, 1000).Select(i => (float)i).ToArray();

        new DepthNormalizer().Normalize(values);

        Assert.Equal(0, values[0]);
        Assert.Equal(1, values[^1]);
        Assert.InRange(values[500], 0.45f, 0.55f);
        Assert.All(values, value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void The_range_follows_a_changing_scene_slowly()
    {
        var normalizer = new DepthNormalizer(follow: 0.2);
        normalizer.Normalize(Enumerable.Range(0, 1000).Select(i => (float)i).ToArray());
        double firstHigh = normalizer.High;

        // everything suddenly twice as close: the range moves a fifth of the way
        normalizer.Normalize(Enumerable.Range(0, 1000).Select(i => (float)i * 2).ToArray());

        Assert.InRange(normalizer.High, firstHigh * 1.15, firstHigh * 1.25);
    }

    [Fact]
    public void Closeness_maps_to_view_depth_like_the_shaders()
    {
        Assert.Equal(1, DepthMath.ViewDepth(1), 3);
        Assert.Equal(DepthMath.FarthestView, DepthMath.ViewDepth(0), 3);
        Assert.True(DepthMath.ViewDepth(0.5f) < DepthMath.ViewDepth(0.25f));
    }

    [Fact]
    public void Focus_is_the_middle_of_the_screen()
    {
        const int width = 40, height = 20;
        var map = new float[width * height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                map[y * width + x] = x is >= 17 and < 23 && y is >= 8 and < 12 ? 0.9f : 0.1f;

        Assert.Equal(0.9f, DepthMath.FocusCloseness(map, width, height));
    }

    [Theory]
    [InlineData(1920, 1080, 392)]
    [InlineData(2560, 1080, 532 > 518 ? 518 : 532)]
    [InlineData(1024, 768, 294)]
    [InlineData(800, 1200, 224)]
    public void The_depth_AI_runs_at_a_size_for_the_window_shape(int width, int height, int expectedWidth)
    {
        var (modelWidth, modelHeight) = DepthEstimator.SizeFor(width, height);

        Assert.Equal(expectedWidth, modelWidth);
        Assert.Equal(DepthEstimator.ModelHeight, modelHeight);
        Assert.Equal(0, modelWidth % 14);
    }
}

public class DepthModelTests
{
    private const string Url = "https://example.test/model.onnx";

    [Fact]
    public async Task The_model_is_downloaded_once_and_checked()
    {
        using var directory = new TempDirectory();
        var bytes = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        var handler = new FakeHttpHandler();
        handler.MapBytes(Url, bytes);
        using var http = new HttpClient(handler);
        var reports = new List<double>();

        string path = await DepthModel.EnsureAsync(http, directory.Path, new SyncProgress(reports.Add), CancellationToken.None,
            Url, Sha256(bytes), bytes.Length);

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(1, reports[^1]);

        await DepthModel.EnsureAsync(http, directory.Path, null, CancellationToken.None, Url, Sha256(bytes), bytes.Length);
        Assert.Equal(1, handler.CountRequests(Url));
    }

    [Fact]
    public async Task A_damaged_download_is_thrown_away()
    {
        using var directory = new TempDirectory();
        var handler = new FakeHttpHandler();
        handler.MapBytes(Url, [1, 2, 3]);
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DepthModel.EnsureAsync(http, directory.Path, null, CancellationToken.None, Url, new string('0', 64), 3));

        Assert.False(File.Exists(DepthModel.PathIn(directory.Path)));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Reports straight away (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

public class PreviewFrameTests
{
    [Fact]
    public void A_kept_frame_reads_back_the_same()
    {
        using var directory = new TempDirectory();
        var frame = new PreviewFrame(4, 2, [.. Enumerable.Range(0, 32).Select(i => (byte)i)], 3, 2, [0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f]);

        frame.Save(directory.Path);
        var loaded = PreviewFrame.Load(directory.Path);

        Assert.True(PreviewFrame.Exists(directory.Path));
        Assert.NotNull(loaded);
        Assert.Equal(frame.Pixels, loaded!.Pixels);
        Assert.Equal(frame.Depth, loaded.Depth);
        Assert.Equal((4, 2, 3, 2), (loaded.Width, loaded.Height, loaded.DepthWidth, loaded.DepthHeight));
    }

    [Fact]
    public void A_missing_or_damaged_frame_is_none()
    {
        using var directory = new TempDirectory();
        Assert.Null(PreviewFrame.Load(directory.Path));

        File.WriteAllBytes(PreviewFrame.PathIn(directory.Path), [1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Null(PreviewFrame.Load(directory.Path));
    }
}

/// <summary>The shaders on Windows' software renderer (WARP), so no graphics card is needed.</summary>
public sealed class EffectRendererTests : IDisposable
{
    private const int Width = 160, Height = 90;

    private readonly ID3D11Device _device;
    private readonly EffectRenderer _renderer;
    private readonly ID3D11Texture2D _output;
    private readonly ID3D11RenderTargetView _outputView;

    public EffectRendererTests()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device).CheckError();
        _device = device!;
        _renderer = new EffectRenderer(_device);
        _renderer.Resize(Width, Height);
        _output = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget));
        _outputView = _device.CreateRenderTargetView(_output);
    }

    [Fact]
    public void With_every_effect_off_the_picture_comes_out_unchanged()
    {
        var picture = Picture((x, y) => ((byte)(x * 255 / Width), (byte)(y * 255 / Height), (byte)((x + y) % 256)));
        Upload(picture);

        _renderer.Render(_outputView, EffectParameters.None, 0);
        var result = Download();

        for (int i = 0; i < picture.Length; i += 4)
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(result[i + channel] - picture[i + channel], -1, 1);
    }

    [Fact]
    public void Contact_shadows_darken_the_corner_where_a_floor_meets_a_wall()
    {
        // a flat grey picture; the depth: a wall in the top half, a floor coming towards the camera below
        Upload(Picture((_, _) => (160, 160, 160)));
        const int depthWidth = 56, depthHeight = 32;
        var depth = new float[depthWidth * depthHeight];

        for (int y = 0; y < depthHeight; y++)
            for (int x = 0; x < depthWidth; x++)
                depth[y * depthWidth + x] = y < depthHeight / 2 ? 0.3f : 0.3f + 0.6f * (y - depthHeight / 2) / (depthHeight / 2f);

        _renderer.UpdateDepth(depth, depthWidth, depthHeight);
        _renderer.Render(_outputView, EffectParameters.None with { AmbientOcclusion = 1, UseDepth = true }, 0);
        var result = Download();

        int corner = Blue(result, Width / 2, Height / 2 + 2);
        int openFloor = Blue(result, Width / 2, Height - 4);
        Assert.True(corner < openFloor - 4, $"in the corner {corner}, on the open floor {openFloor}");
    }

    [Fact]
    public void Glow_spreads_around_bright_spots_only()
    {
        Upload(Picture((x, y) => Math.Abs(x - 80) < 4 && Math.Abs(y - 45) < 4 ? ((byte)255, (byte)255, (byte)255) : ((byte)20, (byte)20, (byte)20)));

        _renderer.Render(_outputView, EffectParameters.None with { Bloom = 1 }, 0);
        var result = Download();

        Assert.True(Blue(result, 90, 45) > 30, "the glow reaches beside the spot");
        Assert.InRange(Blue(result, 5, 5), 18, 23);
    }

    [Fact]
    public void Sun_rays_stream_down_from_a_bright_far_sky()
    {
        // a bright sky above a dark ground; the sky far away, the ground near
        Upload(Picture((_, y) => y < Height / 2 ? ((byte)250, (byte)245, (byte)230) : ((byte)40, (byte)40, (byte)40)));
        const int depthWidth = 56, depthHeight = 32;
        var depth = new float[depthWidth * depthHeight];

        for (int y = 0; y < depthHeight; y++)
            for (int x = 0; x < depthWidth; x++)
                depth[y * depthWidth + x] = y < depthHeight / 2 ? 0 : 0.8f;

        _renderer.UpdateDepth(depth, depthWidth, depthHeight);
        _renderer.Render(_outputView, EffectParameters.None with { UseDepth = true }, 0);
        int without = Blue(Download(), Width / 2, Height / 2 + 8);

        _renderer.ClearHistory();
        _renderer.Render(_outputView, EffectParameters.None with { UseDepth = true, SunRays = 1 }, 0);
        int with = Blue(Download(), Width / 2, Height / 2 + 8);

        Assert.True(with > without + 3, $"below the sky: {without} without rays, {with} with");
    }

    [Fact]
    public void The_preview_draws_a_kept_frame()
    {
        var pixels = Picture((x, y) => ((byte)(x * 255 / Width), (byte)(y * 255 / Height), 90));
        var frame = new PreviewFrame(Width, Height, pixels, 56, 32, new float[56 * 32]);

        using var preview = new EffectsPreview(frame);
        var plain = preview.Render(new EffectSettings { UseDepth = false, AmbientOcclusion = 0, IndirectLight = 0, Haze = 0, Reflections = 0, SunRays = 0,
            Bloom = 0, Contrast = 0, Saturation = 0, Warmth = 0, Vignette = 0, Sharpen = 0 });
        var lit = preview.Render(EffectPresets.Look(EffectPreset.Cinematic)!);

        Assert.Equal(pixels.Length, plain.Length);
        Assert.InRange(plain[Width * 4 * 40 + 80 * 4] - pixels[Width * 4 * 40 + 80 * 4], -1, 1);
        Assert.NotEqual(plain, lit);
    }

    public void Dispose()
    {
        _outputView.Dispose();
        _output.Dispose();
        _renderer.Dispose();
        _device.Dispose();
    }

    private static byte[] Picture(Func<int, int, (byte R, byte G, byte B)> colour)
    {
        var pixels = new byte[Width * Height * 4];

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var (r, g, b) = colour(x, y);
                int i = (y * Width + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
            }

        return pixels;
    }

    private static int Blue(byte[] pixels, int x, int y) => pixels[(y * Width + x) * 4];

    private void Upload(byte[] pixels) => _device.ImmediateContext.UpdateSubresource(pixels, _renderer.Scene, 0, Width * 4);

    private unsafe byte[] Download()
    {
        using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        var context = _device.ImmediateContext;
        context.CopyResource(staging, _output);
        var mapped = context.Map(staging, 0, MapMode.Read);
        var pixels = new byte[Width * Height * 4];

        for (int y = 0; y < Height; y++)
            new ReadOnlySpan<byte>((byte*)mapped.DataPointer + y * mapped.RowPitch, Width * 4).CopyTo(pixels.AsSpan(y * Width * 4));

        context.Unmap(staging, 0);
        return pixels;
    }
}
