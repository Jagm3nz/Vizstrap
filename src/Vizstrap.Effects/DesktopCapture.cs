using Vizstrap.Core.Logging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Vizstrap.Effects;

internal enum CaptureResult
{
    NewFrame,
    /// <summary>Nothing new on the screen in time (or only the mouse moved).</summary>
    NoFrame,
    /// <summary>The screen changed (resolution, a UAC prompt…); capturing starts again next time.</summary>
    Lost,
}

/// <summary>
/// Copies part of the screen into a texture, all on the graphics card, with DXGI Desktop Duplication:
/// the same way OBS captures a display. Nothing reads from or writes to Roblox.
/// </summary>
internal sealed class DesktopCapture(ID3D11Device device) : IDisposable
{
    private const string LogSource = nameof(DesktopCapture);

    private IDXGIOutputDuplication? _duplication;
    private Vortice.RawRect _outputArea;

    /// <summary>Copies <paramref name="area"/> (screen pixels) into the top-left of <paramref name="target"/>.</summary>
    public CaptureResult TryCapture(Native.Rect area, ID3D11Texture2D target, uint timeoutMs)
    {
        if (!EnsureDuplication(area))
            return CaptureResult.Lost;

        var result = _duplication!.AcquireNextFrame(timeoutMs, out var info, out var resource);

        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            return CaptureResult.NoFrame;

        if (result == Vortice.DXGI.ResultCode.AccessLost)
        {
            Reset();
            return CaptureResult.Lost;
        }

        result.CheckError();

        try
        {
            // only the pointer moved: the picture is what we have
            if (info.LastPresentTime == 0)
                return CaptureResult.NoFrame;

            int left = Math.Max(area.Left, _outputArea.Left), top = Math.Max(area.Top, _outputArea.Top);
            int right = Math.Min(area.Right, _outputArea.Right), bottom = Math.Min(area.Bottom, _outputArea.Bottom);

            if (right <= left || bottom <= top)
                return CaptureResult.NoFrame;

            using var texture = resource!.QueryInterface<ID3D11Texture2D>();
            device.ImmediateContext.CopySubresourceRegion(target, 0,
                (uint)(left - area.Left), (uint)(top - area.Top), 0, texture, 0,
                new Box(left - _outputArea.Left, top - _outputArea.Top, 0, right - _outputArea.Left, bottom - _outputArea.Top, 1));
            return CaptureResult.NewFrame;
        }
        finally
        {
            resource?.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    public void Dispose() => Reset();

    /// <summary>Duplicates the display the area's centre is on (one of this device's adapter's outputs).</summary>
    private bool EnsureDuplication(Native.Rect area)
    {
        int x = (area.Left + area.Right) / 2, y = (area.Top + area.Bottom) / 2;

        if (_duplication is not null && x >= _outputArea.Left && x < _outputArea.Right && y >= _outputArea.Top && y < _outputArea.Bottom)
            return true;

        Reset();

        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();

        for (uint index = 0; adapter.EnumOutputs(index, out var output).Success; index++)
        {
            using (output)
            {
                var bounds = output.Description.DesktopCoordinates;

                if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)
                    continue;

                try
                {
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    _duplication = output1.DuplicateOutput(device);
                    _outputArea = bounds;
                    Log.Info(LogSource, $"Capturing {output.Description.DeviceName} ({bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top})");
                    return true;
                }
                catch (SharpGen.Runtime.SharpGenException ex)
                {
                    // e.g. too many captures at once, or the display is being switched
                    Log.Warn(LogSource, $"Can't capture {output.Description.DeviceName}: {ex.Message}");
                    return false;
                }
            }
        }

        return false;
    }

    private void Reset()
    {
        _duplication?.Dispose();
        _duplication = null;
    }
}
