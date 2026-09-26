using System.Windows;
using System.Windows.Media;

namespace Vizstrap.Views.Loading;

/// <summary>The ~2014 Roblox bootstrapper: white panel, big icon, grey rounded Cancel button.</summary>
public partial class Roblox2014LoadingWindow : Window
{
    public Roblox2014LoadingWindow(bool isDark)
    {
        CancelBackground = Brush(isDark ? "#3A3C3F" : "#F2F2F2");
        CancelHover = Brush(isDark ? "#46484C" : "#E4E4E4");
        CancelBorder = Brush(isDark ? "#4E5054" : "#C6C6C6");

        InitializeComponent();

        CancelButton.Foreground = Brush(isDark ? "#C4C5C4" : "#4B4B4B");

        // Bloxstrap's dark variant of this dialog
        if (isDark)
        {
            Background = Brush("#191B1D");
            Panel.Background = Brush("#232527");
            Message.Foreground = Brushes.White;
        }
    }

    public Brush CancelBackground { get; }

    public Brush CancelHover { get; }

    public Brush CancelBorder { get; }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
