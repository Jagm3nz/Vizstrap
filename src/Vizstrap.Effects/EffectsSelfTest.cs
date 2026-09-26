using System.Diagnostics;
using Vizstrap.Core.Logging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Vizstrap.Effects;

/// <summary>
/// "-effectstest": checks the picture effects on this computer without any window: the shaders compile
/// and draw a frame on the graphics card, and (when the model is there) the depth AI runs once.
/// </summary>
public static class EffectsSelfTest
{
    private const string LogSource = nameof(EffectsSelfTest);

    /// <returns>True when everything that could be tried worked; the log has the details.</returns>
    public static bool Run(string? modelPath)
    {
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device).CheckError();
            using var _ = device!;
            using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            var clock = Stopwatch.StartNew();
            using var renderer = new EffectRenderer(device);
            renderer.Resize(640, 360);
            Log.Info(LogSource, $"Shaders ready on {adapter.Description.Description} in {clock.ElapsedMilliseconds} ms");

            using var output = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, 640, 360, 1, 1, BindFlags.RenderTarget));
            using var outputView = device.CreateRenderTargetView(output);
            renderer.Render(outputView, EffectParameters.None with { Bloom = 0.5f, Contrast = 0.3f }, 0);

            if (modelPath is null || !File.Exists(modelPath))
            {
                Log.Info(LogSource, "Frame drawn; no depth AI to try");
                return true;
            }

            var (width, height) = DepthEstimator.SizeFor(640, 360);
            renderer.DrawModelInput(width, height);
            using var staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
                BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            device.ImmediateContext.CopyResource(staging, renderer.ModelInput!);
            var mapped = device.ImmediateContext.Map(staging, 0, MapMode.Read);

            using var estimator = new DepthEstimator(modelPath, width, height);
            estimator.TrySubmit(mapped.DataPointer, (int)mapped.RowPitch);
            device.ImmediateContext.Unmap(staging, 0);

            var waited = Stopwatch.StartNew();
            float[]? depth = null;

            while (waited.Elapsed < TimeSpan.FromSeconds(30) && !estimator.TryTake(out depth))
                Thread.Sleep(10);

            if (depth is null)
            {
                Log.Warn(LogSource, "The depth AI gave no answer in 30 s");
                return false;
            }

            renderer.UpdateDepth(depth, width, height);
            renderer.Render(outputView, EffectParameters.None with { AmbientOcclusion = 1, IndirectLight = 1, UseDepth = true }, 0);
            Log.Info(LogSource, $"Depth AI works ({estimator.LastMilliseconds:F0} ms for the first map at {width}x{height})");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(LogSource, ex);
            return false;
        }
    }
}
