using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Logging;
using Vizstrap.Effects;
using Vizstrap.ViewModels;

namespace Vizstrap.Views;

/// <summary>
/// Before and after on the frame kept from the player's last game, redrawn as the sliders on the Shaders
/// page move (the window stays open beside the settings).
/// </summary>
public partial class EffectsPreviewWindow : NeonWindow
{
    private readonly EffectsViewModel _effects;
    private readonly EffectsPreview _preview;
    private readonly WriteableBitmap _after;
    private readonly DispatcherTimer _redraw;

    private EffectsPreviewWindow(EffectsViewModel effects, PreviewFrame frame)
    {
        InitializeComponent();
        _effects = effects;
        _preview = new EffectsPreview(frame, [.. App.EnabledPackages().SelectMany(package => package.Effects)]);

        Before.Source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
        _after = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        After.Source = _after;
        Divider.Height = frame.Height;

        // redrawn a moment after the last slider move, not on every step of a drag
        _redraw = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => Redraw(), Dispatcher);
        _effects.PropertyChanged += OnEffectsChanged;
        Closed += (_, _) =>
        {
            _redraw.Stop();
            _effects.PropertyChanged -= OnEffectsChanged;
            _preview.Dispose();
        };

        Redraw();
        UpdateSplit();
    }

    /// <summary>Opens the preview beside the settings; false when there's no frame to show yet.</summary>
    public static bool TryShow(EffectsViewModel effects, Window? owner)
    {
        if (PreviewFrame.Load(App.Paths.Effects) is not { } frame)
            return false;

        try
        {
            new EffectsPreviewWindow(effects, frame) { Owner = owner }.Show();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(nameof(EffectsPreviewWindow), ex);
            MessageWindow.ShowError(Localization.Strings.Error_Unexpected, ex);
            return true;
        }
    }

    private void OnEffectsChanged(object? sender, PropertyChangedEventArgs e)
    {
        _redraw.Stop();
        _redraw.Start();
    }

    private void Redraw()
    {
        _redraw.Stop();
        byte[] pixels = _preview.Render(_effects.Current());
        _after.WritePixels(new Int32Rect(0, 0, _preview.Width, _preview.Height), pixels, _preview.Width * 4, 0);
    }

    private void OnSplitChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSplit();

    private void UpdateSplit()
    {
        if (_after is null)
            return;

        double x = Split.Value * _preview.Width;
        After.Clip = new RectangleGeometry(new Rect(x, 0, _preview.Width - x, _preview.Height));
        Divider.Margin = new Thickness(x - 1, 0, 0, 0);
    }
}
