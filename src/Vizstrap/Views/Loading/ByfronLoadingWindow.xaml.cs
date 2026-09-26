using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vizstrap.Views.Loading;

/// <summary>Roblox's 2023 bootstrapper look; the light variant matches the Roblox website's light theme.</summary>
public partial class ByfronLoadingWindow : Window
{
    public ByfronLoadingWindow(bool isDark)
    {
        InitializeComponent();

        if (isDark)
            return;

        var foreground = Brush("#393B3D");

        Frame.Background = Brush("#F2F4F5");
        Frame.BorderThickness = new Thickness(1);
        Message.Foreground = foreground;
        Wordmark.Fill = foreground;
        CloseGlyph.Fill = foreground;
        Bar.Foreground = foreground;
        Bar.Background = Brush("#BDBEBE");
        Logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Byfron/ByfronLogoLight.jpg"));
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
