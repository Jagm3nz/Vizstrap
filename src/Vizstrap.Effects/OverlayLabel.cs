using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Vizstrap.Effects;

/// <summary>
/// A short note drawn at the top of the overlay for a moment (the look picked with F7), with
/// Direct2D straight onto the frame about to be shown.
/// </summary>
internal sealed class OverlayLabel : IDisposable
{
    private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(1.6);

    private readonly ID2D1Factory1 _factory;
    private readonly ID2D1Device _device;
    private readonly ID2D1DeviceContext _context;
    private readonly IDWriteFactory _writeFactory;
    private readonly IDWriteTextFormat _format;
    private readonly ID2D1SolidColorBrush _text;
    private readonly ID2D1SolidColorBrush _background;
    private readonly ID2D1SolidColorBrush _accent;

    private string? _message;
    private DateTime _until;

    public OverlayLabel(ID3D11Device device)
    {
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            _device = _factory.CreateDevice(dxgiDevice);
        _context = _device.CreateDeviceContext(DeviceContextOptions.None);

        _writeFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _format = _writeFactory.CreateTextFormat("Segoe UI", FontWeight.SemiBold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, 18);
        _format.TextAlignment = TextAlignment.Center;
        _format.ParagraphAlignment = ParagraphAlignment.Center;

        _text = _context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _background = _context.CreateSolidColorBrush(new Color4(0.08f, 0.07f, 0.16f, 0.82f));
        _accent = _context.CreateSolidColorBrush(new Color4(0.5f, 0.47f, 0.87f, 1));
    }

    public bool IsShowing => _message is not null && DateTime.UtcNow < _until;

    public void Show(string message)
    {
        _message = message;
        _until = DateTime.UtcNow + ShownFor;
    }

    /// <summary>Draws the note onto the swap chain's back buffer, if one is showing.</summary>
    public void Draw(IDXGISwapChain1 swapChain, int width)
    {
        if (!IsShowing)
            return;

        using var surface = swapChain.GetBuffer<IDXGISurface>(0);
        using var bitmap = _context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw));

        _context.Target = bitmap;
        _context.BeginDraw();

        const float boxWidth = 300, boxHeight = 44;
        var box = new Rect((width - boxWidth) / 2, 70, boxWidth, boxHeight);
        _context.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(box.X, box.Y, box.Width, box.Height), 10, 10), _background);
        _context.FillRectangle(new Rect(box.Left + 12, box.Bottom - 3, box.Width - 24, 2), _accent);
        _context.DrawText(_message!, _format, box, _text);

        _context.EndDraw();
        _context.Target = null;
    }

    public void Dispose()
    {
        _accent.Dispose();
        _background.Dispose();
        _text.Dispose();
        _format.Dispose();
        _writeFactory.Dispose();
        _context.Dispose();
        _device.Dispose();
        _factory.Dispose();
    }
}
