using System.Runtime.InteropServices;
using System.Text;

namespace Vizstrap.Effects;

/// <summary>The Win32 calls for the overlay window and for finding Roblox's window.</summary>
internal static class Native
{
    public const uint WsPopup = 0x80000000;
    public const uint WsExTopmost = 0x00000008;
    public const uint WsExTransparent = 0x00000020;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExLayered = 0x00080000;
    public const uint WsExNoRedirectionBitmap = 0x00200000;
    public const uint WsExNoActivate = 0x08000000;
    public const uint LwaAlpha = 0x2;
    public const uint WdaExcludeFromCapture = 0x11;
    public const uint WmHotKey = 0x0312;
    public const uint VkF7 = 0x76;
    public const uint VkF8 = 0x77;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr DpiAwarenessPerMonitorV2 = new(-4);

    public delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint Size, Style;
        public IntPtr WndProc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public Point Point;
    }

    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassExW(ref WndClassEx windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool PeekMessageW(out Msg message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref Msg message);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int size);

    /// <summary>The window's drawing area on the screen, in physical pixels (the calling thread must be DPI aware).</summary>
    public static bool TryGetClientArea(IntPtr hwnd, out Rect area)
    {
        area = default;

        if (!GetClientRect(hwnd, out var client))
            return false;

        var origin = new Point();

        if (!ClientToScreen(hwnd, ref origin))
            return false;

        area = new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
        return client.Right > 0 && client.Bottom > 0;
    }

    /// <summary>
    /// The player's game window of a Roblox process: class "WINDOWSCLIENT", or failing that its biggest
    /// visible window.
    /// </summary>
    public static IntPtr FindGameWindow(int processId)
    {
        IntPtr found = IntPtr.Zero, biggest = IntPtr.Zero;
        long biggestArea = 0;
        var name = new StringBuilder(64);

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);

            if (owner != processId || !IsWindowVisible(hwnd))
                return true;

            name.Clear();
            GetClassNameW(hwnd, name, name.Capacity);

            // Vizstrap's own overlay, when the effects run in the game's process (tests)
            if (name.ToString() == "VizstrapEffectsOverlay")
                return true;

            if (name.ToString() == "WINDOWSCLIENT")
            {
                found = hwnd;
                return false;
            }

            if (GetClientRect(hwnd, out var client) && (long)client.Right * client.Bottom > biggestArea)
            {
                biggestArea = (long)client.Right * client.Bottom;
                biggest = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero ? found : biggest;
    }
}
