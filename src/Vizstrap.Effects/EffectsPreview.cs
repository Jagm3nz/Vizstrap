using Vizstrap.Core.Effects;
using Vizstrap.Core.Packages;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Vizstrap.Effects;

/// <summary>
/// Draws the effects on a kept frame from the player's game (<see cref="PreviewFrame"/>) for the
/// settings' preview, off screen: the graphics card when there is one, Windows' software renderer if not.
/// </summary>
public sealed class EffectsPreview : IDisposable
{
    /// <summary>Frames drawn per picture, so the effects averaged over frames settle as they do in game.</summary>
    private const int SettleFrames = 10;

    private readonly ID3D11Device _device;
    private readonly EffectRenderer _renderer;
    private readonly ID3D11Texture2D _output;
    private readonly ID3D11RenderTargetView _outputView;
    private readonly ID3D11Texture2D _readBack;

    public EffectsPreview(PreviewFrame frame, IReadOnlyList<CustomEffect>? customEffects = null)
    {
        Width = frame.Width;
        Height = frame.Height;

        if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device).Failure)
            D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out device).CheckError();

        _device = device!;
        _renderer = new EffectRenderer(_device);
        _renderer.Resize(Width, Height);
        CustomEffectSources.Load(_renderer, customEffects);
        _device.ImmediateContext.UpdateSubresource(frame.Pixels, _renderer.Scene, 0, (uint)(Width * 4));
        _renderer.UpdateDepth(frame.Depth, frame.DepthWidth, frame.DepthHeight);
        _renderer.FocusDepth = DepthMath.ViewDepth(DepthMath.FocusCloseness(frame.Depth, frame.DepthWidth, frame.DepthHeight));

        _output = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)Width, (uint)Height, 1, 1, BindFlags.RenderTarget));
        _outputView = _device.CreateRenderTargetView(_output);
        _readBack = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)Width, (uint)Height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The frame with these settings' effects, as B8G8R8A8 rows.</summary>
    public unsafe byte[] Render(EffectSettings settings)
    {
        var parameters = EffectParameters.From(settings);
        _renderer.ClearHistory();

        for (int frame = 0; frame < SettleFrames; frame++)
            _renderer.Render(_outputView, parameters, frame * 0.016f,
                [.. settings.Custom.Where(state => state.Enabled).Select(state => new CustomPass(state.Id, state.Values))]);

        var context = _device.ImmediateContext;
        context.CopyResource(_readBack, _output);
        var mapped = context.Map(_readBack, 0, MapMode.Read);
        var pixels = new byte[Width * Height * 4];

        try
        {
            for (int y = 0; y < Height; y++)
                new ReadOnlySpan<byte>((byte*)mapped.DataPointer + y * mapped.RowPitch, Width * 4).CopyTo(pixels.AsSpan(y * Width * 4));
        }
        finally
        {
            context.Unmap(_readBack, 0);
        }

        return pixels;
    }

    public void Dispose()
    {
        _readBack.Dispose();
        _outputView.Dispose();
        _output.Dispose();
        _renderer.Dispose();
        _device.Dispose();
    }
}
