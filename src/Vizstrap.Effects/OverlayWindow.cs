using System.Runtime.InteropServices;

namespace Vizstrap.Effects;

[Flags]
internal enum OverlayKeys
{
    None = 0,
    /// <summary>F8: effects off and on.</summary>
    Toggle = 1,
    /// <summary>F7: the next look.</summary>
    NextLook = 2,
}

/// <summary>
/// The window the lit picture is shown in, over Roblox: always on top, never focused, clicks go
/// through it to the game, and left out of screen captures (so it doesn't capture itself, and
/// recordings show the game as it is).
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "VizstrapEffectsOverlay";
    private const int ToggleKeyId = 0x5646;
    private const int NextLookKeyId = 0x5647;

    private static readonly Native.WndProc Procedure = Native.DefWindowProcW;
    private static bool _classRegistered;

    private Native.Rect _placed;
    private bool _visible;
    private bool _keysRegistered;

    public OverlayWindow(bool excludeFromCapture = true)
    {
        var instance = Native.GetModuleHandleW(null);

        if (!_classRegistered)
        {
            var windowClass = new Native.WndClassEx
            {
                Size = (uint)Marshal.SizeOf<Native.WndClassEx>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = instance,
                ClassName = ClassName,
            };

            if (Native.RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410 /* already there */)
                throw new InvalidOperationException($"The overlay window class can't be registered ({Marshal.GetLastWin32Error()}).");

            _classRegistered = true;
        }

        const uint exStyle = Native.WsExTopmost | Native.WsExNoActivate | Native.WsExToolWindow | Native.WsExLayered |
            Native.WsExTransparent | Native.WsExNoRedirectionBitmap;
        Handle = Native.CreateWindowExW(exStyle, ClassName, "Vizstrap effects", Native.WsPopup, 0, 0, 16, 16,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException($"The overlay window can't be created ({Marshal.GetLastWin32Error()}).");

        // a layered window at full opacity is what lets clicks through (with WS_EX_TRANSPARENT)
        Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LwaAlpha);

        if (excludeFromCapture)
            Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture);
    }

    public IntPtr Handle { get; }

    public bool IsVisible => _visible;

    /// <summary>Puts the window over the area and shows it, without taking focus.</summary>
    public void Show(Native.Rect area)
    {
        if (_visible && area.Equals(_placed))
            return;

        Native.SetWindowPos(Handle, Native.HwndTopmost, area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top,
            Native.SwpNoActivate | Native.SwpShowWindow);
        _placed = area;
        _visible = true;
    }

    public void Hide()
    {
        if (!_visible)
            return;

        Native.ShowWindow(Handle, Native.SwHide);
        _visible = false;
    }

    /// <summary>F8 and F7 work only while Roblox is in front, so other apps keep their keys.</summary>
    public void SetKeys(bool active)
    {
        if (active == _keysRegistered)
            return;

        if (active)
        {
            Native.RegisterHotKey(Handle, ToggleKeyId, 0, Native.VkF8);
            Native.RegisterHotKey(Handle, NextLookKeyId, 0, Native.VkF7);
        }
        else
        {
            Native.UnregisterHotKey(Handle, ToggleKeyId);
            Native.UnregisterHotKey(Handle, NextLookKeyId);
        }

        _keysRegistered = active;
    }

    /// <summary>Handles the window's messages; the keys pressed since the last call.</summary>
    public OverlayKeys Pump()
    {
        var pressed = OverlayKeys.None;

        while (Native.PeekMessageW(out var message, IntPtr.Zero, 0, 0, 1))
        {
            if (message.Message == Native.WmHotKey)
            {
                if (message.WParam == ToggleKeyId)
                    pressed |= OverlayKeys.Toggle;
                else if (message.WParam == NextLookKeyId)
                    pressed |= OverlayKeys.NextLook;
            }

            Native.TranslateMessage(ref message);
            Native.DispatchMessageW(ref message);
        }

        return pressed;
    }

    public void Dispose()
    {
        SetKeys(false);
        Native.DestroyWindow(Handle);
    }
}
