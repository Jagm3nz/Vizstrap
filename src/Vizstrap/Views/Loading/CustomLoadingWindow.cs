using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Logging;
using Vizstrap.ViewModels;

namespace Vizstrap.Views.Loading;

/// <summary>
/// The "Custom" style: the loading window as put together on the Appearance page (background colour
/// or picture, text and bar colours, one of four layouts, size).
/// </summary>
public sealed class CustomLoadingWindow : NeonWindow
{
    private readonly CustomLoadingTheme _theme;
    private readonly Brush _text;
    private readonly Brush _mutedText;

    public CustomLoadingWindow(CustomLoadingTheme theme)
    {
        _theme = theme;

        var (width, height) = CustomLoadingTheme.Dimensions(theme.Size);
        Width = width;
        Height = height;
        ResizeMode = ResizeMode.NoResize;

        var textColor = ColorOr(theme.TextColor, Colors.White);
        _text = Frozen(new SolidColorBrush(textColor));
        _mutedText = Frozen(new SolidColorBrush(textColor) { Opacity = 0.75 });

        // the bar's style reads these two, so a colour of the theme's own replaces the accent here only
        if (CustomLoadingTheme.IsColor(theme.BarColor))
            Resources["NeonAccentBrush"] = Frozen(new SolidColorBrush(ColorOr(theme.BarColor, Colors.White)));

        Resources["NeonTrackBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x40, textColor.R, textColor.G, textColor.B)));

        var root = new Grid();
        root.Children.Add(new Rectangle { Fill = Frozen(new SolidColorBrush(ColorOr(theme.BackgroundColor, Color.FromRgb(0x15, 0x12, 0x2A)))) });

        if (LoadPicture(theme.BackgroundImage) is { } picture)
        {
            root.Children.Add(new Image
            {
                Source = picture,
                Stretch = theme.FillImage ? Stretch.UniformToFill : Stretch.Uniform,
            });

            root.Children.Add(new Rectangle
            {
                Fill = Brushes.Black,
                Opacity = Math.Clamp(theme.Dim, 0, CustomLoadingTheme.MaxDim) / 100.0,
            });
        }

        root.Children.Add(theme.Layout switch
        {
            ThemeLayout.Bottom => BottomLayout(),
            ThemeLayout.Side => SideLayout(height),
            ThemeLayout.Minimal => MinimalLayout(),
            _ => CenteredLayout(height),
        });

        Content = root;
    }

    private UIElement CenteredLayout(double height)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 0) };

        if (_theme.ShowIcon)
            panel.Children.Add(IconOf(height * 0.2, new Thickness(0, 0, 0, 14), HorizontalAlignment.Center));

        panel.Children.Add(Status(17, TextAlignment.Center));
        panel.Children.Add(Detail(TextAlignment.Center, new Thickness(0, 4, 0, 14)));
        panel.Children.Add(Bar(8));
        panel.Children.Add(Cancel(HorizontalAlignment.Center, new Thickness(0, 10, 0, 0)));
        return panel;
    }

    private UIElement BottomLayout()
    {
        var band = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0))),
            Padding = new Thickness(18, 14, 18, 16),
        };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

        if (_theme.ShowIcon)
        {
            var icon = IconOf(40, new Thickness(0, 0, 14, 0), HorizontalAlignment.Left);
            DockPanel.SetDock(icon, Dock.Left);
            top.Children.Add(icon);
        }

        var cancel = Cancel(HorizontalAlignment.Right, new Thickness(12, 0, 0, 0));
        DockPanel.SetDock(cancel, Dock.Right);
        top.Children.Add(cancel);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Status(16, TextAlignment.Left));
        texts.Children.Add(Detail(TextAlignment.Left, new Thickness(0, 2, 0, 0)));
        top.Children.Add(texts);

        var content = new StackPanel();
        content.Children.Add(top);
        content.Children.Add(Bar(8));
        band.Child = content;
        return band;
    }

    private UIElement SideLayout(double height)
    {
        var grid = new Grid { Margin = new Thickness(28, 0, 28, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = _theme.ShowIcon ? new GridLength(0.38, GridUnitType.Star) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star) });

        if (_theme.ShowIcon)
            grid.Children.Add(IconOf(height * 0.42, new Thickness(0, 0, 24, 0), HorizontalAlignment.Center));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(texts, 1);
        texts.Children.Add(Status(18, TextAlignment.Left));
        texts.Children.Add(Detail(TextAlignment.Left, new Thickness(0, 4, 0, 14)));
        texts.Children.Add(Bar(8));
        texts.Children.Add(Cancel(HorizontalAlignment.Left, new Thickness(-14, 10, 0, 0)));
        grid.Children.Add(texts);
        return grid;
    }

    private UIElement MinimalLayout()
    {
        var grid = new Grid();

        var line = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 10, 12) };
        var cancel = Cancel(HorizontalAlignment.Right, new Thickness(0));
        DockPanel.SetDock(cancel, Dock.Right);
        line.Children.Add(cancel);
        line.Children.Add(Status(13, TextAlignment.Left));
        grid.Children.Add(line);

        var bar = Bar(4);
        bar.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(bar);
        return grid;
    }

    // ---- parts, bound to the LoadingViewModel like the other styles

    private Image IconOf(double size, Thickness margin, HorizontalAlignment alignment)
    {
        var image = new Image { Width = size, Height = size, Margin = margin, HorizontalAlignment = alignment };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(LoadingViewModel.IconImage)));
        return image;
    }

    private TextBlock Status(double size, TextAlignment alignment)
    {
        var text = new TextBlock
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = _text,
            TextAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LoadingViewModel.Status)));
        return text;
    }

    private TextBlock Detail(TextAlignment alignment, Thickness margin)
    {
        var text = new TextBlock { FontSize = 12, Foreground = _mutedText, TextAlignment = alignment, Margin = margin };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LoadingViewModel.Detail)) { TargetNullValue = " " });
        return text;
    }

    private ProgressBar Bar(double height)
    {
        var bar = new ProgressBar();
        bar.SetResourceReference(StyleProperty, "NeonProgressBar");
        bar.Height = height;
        bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(LoadingViewModel.Progress)) { Mode = BindingMode.OneWay });
        bar.SetBinding(ProgressBar.IsIndeterminateProperty, new Binding(nameof(LoadingViewModel.IsIndeterminate)));
        return bar;
    }

    private Wpf.Ui.Controls.Button Cancel(HorizontalAlignment alignment, Thickness margin)
    {
        var button = new Wpf.Ui.Controls.Button
        {
            Content = Localization.Strings.Common_Cancel,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 4, 14, 4),
            FontSize = 12,
            Foreground = _mutedText,
            HorizontalAlignment = alignment,
            Margin = margin,
        };
        button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(nameof(LoadingViewModel.CancelCommand)));
        button.SetBinding(VisibilityProperty, new Binding(nameof(LoadingViewModel.CanCancel)) { Converter = new BoolToHiddenConverter() });
        return button;
    }

    private static BitmapImage? LoadPicture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            // a broken picture never stops Roblox from launching
            Log.Warn(nameof(CustomLoadingWindow), $"Background picture can't be loaded: {path} ({ex.Message})");
            return null;
        }
    }

    private static Color ColorOr(string? hex, Color fallback) =>
        CustomLoadingTheme.IsColor(hex) ? (Color)ColorConverter.ConvertFromString(hex) : fallback;

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
